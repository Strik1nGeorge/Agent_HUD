import test from 'node:test';
import assert from 'node:assert/strict';
import { usageOf, newSession, consumeCodex, consumeClaude, sessionRecords, codexLimits, claudeLimits, windowLabel } from '../lib/metrics.mjs';

test('Codex cached input and reasoning are subsets, not additional usage', () => {
  assert.deepEqual(usageOf({ input_tokens: 1000, cached_input_tokens: 800, output_tokens: 100, reasoning_output_tokens: 70, total_tokens: 1100 }), { input:1000,cached:800,cacheWrite:0,output:100,reasoning:70,total:1100 });
});
test('Claude input includes the separately reported cache categories exactly once', () => {
  const u = usageOf({ input_tokens: 10, output_tokens: 20, cache_read_input_tokens: 1000, cache_creation_input_tokens: 400, output_tokens_details:{thinking_tokens:12} },'claude');
  assert.equal(u.input,1410); assert.equal(u.total,1430); assert.equal(u.reasoning,12);
});
test('Claude streaming repeats coalesce by message ID and preserve the largest counters', () => {
  const s=newSession('claude','file');
  for(const out of [10,30,20])consumeClaude(s,{type:'assistant',sessionId:'s',timestamp:'2026-10-14T10:00:00Z',message:{id:'m',model:'claude',usage:{input_tokens:5,output_tokens:out,cache_read_input_tokens:200}}});
  assert.equal(sessionRecords(s).length,1);assert.equal(sessionRecords(s)[0].total,235);
});
test('Each request keeps the thinking effort it ran with; unknown values stay null', () => {
  const c=newSession('codex','file');consumeCodex(c,{type:'session_meta',payload:{id:'s'}});
  consumeCodex(c,{type:'token_usage_record',timestamp:'2026-10-14T10:00:00Z',payload:{thread_id:'s',response_id:'r0',usage:{input_tokens:1}}});
  consumeCodex(c,{type:'turn_context',payload:{model:'gpt',effort:'xhigh'}});
  consumeCodex(c,{type:'token_usage_record',timestamp:'2026-10-14T10:01:00Z',payload:{thread_id:'s',response_id:'r1',usage:{input_tokens:1}}});
  consumeCodex(c,{type:'turn_context',payload:{model:'gpt',collaboration_mode:{settings:{reasoning_effort:'Low'}}}});
  consumeCodex(c,{type:'token_usage_record',timestamp:'2026-10-14T10:02:00Z',payload:{thread_id:'s',response_id:'r2',usage:{input_tokens:1}}});
  assert.deepEqual(sessionRecords(c).map(r=>r.effort),[null,'xhigh','low']);
  const a=newSession('claude','file');
  const reply=(id,extra)=>consumeClaude(a,{type:'assistant',sessionId:'s',timestamp:'2026-10-14T10:00:00Z',...extra,message:{id,model:'claude',usage:{input_tokens:1,output_tokens:1}}});
  reply('m1',{effort:'max'});reply('m1',{});reply('m2',{perTurnEffort:'medium'});reply('m3',{effort:{level:'odd'}});
  assert.deepEqual(sessionRecords(a).map(r=>r.effort),['max','medium',null]);
});
test('Codex ignores inherited records belonging to a parent and deduplicates response IDs', () => {
  const s=newSession('codex','file');consumeCodex(s,{type:'session_meta',payload:{id:'child',parent_thread_id:'parent',timestamp:'2026-10-14T10:00:00Z'}});
  for(const thread of ['parent','child','child'])consumeCodex(s,{type:'token_usage_record',timestamp:'2026-10-14T10:01:00Z',payload:{thread_id:thread,response_id:'r',usage:{input_tokens:100,output_tokens:5}}});
  assert.equal(sessionRecords(s).length,1);assert.equal(sessionRecords(s)[0].total,105);assert.equal(s.kind,'subagent');
});
test('Legacy cumulative counts are deltas and repeated updates do not add usage', () => {
  const s=newSession('codex','file');consumeCodex(s,{type:'session_meta',payload:{id:'s'}});
  for(const [i,n] of [100,100,160].entries())consumeCodex(s,{type:'event_msg',ordinal:i,timestamp:`2026-10-14T10:0${i}:00Z`,payload:{type:'token_count',info:{total_token_usage:{input_tokens:n}}}});
  assert.deepEqual(sessionRecords(s).map(r=>r.total),[100,60]);
});
test('Legacy telemetry before a format upgrade remains included without double counting new records', () => {
  const s=newSession('codex','file');consumeCodex(s,{type:'session_meta',payload:{id:'s'}});
  consumeCodex(s,{type:'event_msg',ordinal:1,timestamp:'2026-10-14T10:00:00Z',payload:{type:'token_count',info:{total_token_usage:{input_tokens:100}}}});
  consumeCodex(s,{type:'token_usage_record',timestamp:'2026-10-14T10:01:00Z',payload:{thread_id:'s',response_id:'r',usage:{input_tokens:60}}});
  consumeCodex(s,{type:'event_msg',ordinal:3,timestamp:'2026-10-14T10:01:01Z',payload:{type:'token_count',info:{total_token_usage:{input_tokens:160}}}});
  assert.equal(sessionRecords(s).reduce((n,r)=>n+r.total,0),160);
});
test('Missing quota is unknown while zero percent used means 100 percent remaining', () => {
  assert.equal(codexLimits(null,0,'test'),null);
  assert.deepEqual(codexLimits({primary:null},1,'test').windows,[]);
  const result=codexLimits({primary:{used_percent:0,window_minutes:300,resets_at:123}},1,'test');
  assert.equal(result.windows[0].remainingPercent,100);assert.equal(result.windows[0].resetsAt,123000);
  assert.equal(claudeLimits({five_hour:{used_percentage:107,resets_at:123}},1).windows[0].remainingPercent,0);
});
test('Quota windows are named by their length, including the free plan\'s 30-day window', () => {
  const free=codexLimits({rateLimits:{primary:{usedPercent:0,windowDurationMins:43200,resetsAt:1795000000}},planType:'free'},1,'api');
  assert.deepEqual(free.windows.map(w=>[w.name,w.windowMinutes,w.remainingPercent]),[['30 天',43200,100]]);
  assert.deepEqual([300,10080,90,45].map(windowLabel),['5 小时','7 天','90 分钟','45 分钟']);
  assert.equal(windowLabel(120),'2 小时');
});
test('All named Codex quota buckets are retained', () => {
  const result=codexLimits({rateLimits:{primary:{usedPercent:99}},rateLimitsByLimitId:{codex:{primary:{usedPercent:20,windowDurationMins:300}},special:{limitName:'Special',secondary:{usedPercent:50,windowDurationMins:10080}}}},1,'api');
  assert.equal(result.windows.length,2);assert.equal(result.windows[0].remainingPercent,80);assert.match(result.windows[1].name,/Special/);
});
