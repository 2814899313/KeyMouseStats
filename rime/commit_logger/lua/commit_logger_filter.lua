-- commit_logger_filter.lua —— 候选流观察器（只读，不改变候选）
--
-- 为什么需要它：这版 librime/Weasel 没有把 Menu 暴露给 Lua
-- （ctx.menu 在整个输入过程里恒为 nil，也没有 ctx:get_candidate_at），
-- 所以拿不到“当前高亮是第几个候选”。
--
-- 但 filter 是唯一能按顺序看到候选流的地方：input:iter() 依次吐出
-- 当前这次输入的全部候选项。于是这里边遍历边记「明文 → 全局序号(0 起)」，
-- 上屏时按明文反查即可。候选原样放行，顺序、内容都不动。

local cl = require("commit_logger")

local function filter(input, env)
  local ctx = env and env.engine and env.engine.context
  local map = {}

  -- 用归一化输入串给这份快照打标记：多引擎并存时靠它认出“属于哪次输入”；
  -- 归一化是因为上屏那一刻 input 可能多出一个音节分隔空格。
  local raw = (ctx and ctx.input) or ""
  map.__input = raw:gsub("%s+", "")
  if cl and cl.note_map then cl.note_map(map) end

  local i = 0
  for cand in input:iter() do
    local t = cand.text
    if t and map[t] == nil then
      map[t] = i
      if cl and cl.note_map then cl.note_map(map) end
    end
    i = i + 1
    yield(cand)
  end
end

return filter
