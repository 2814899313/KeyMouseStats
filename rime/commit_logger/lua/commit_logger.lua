--[[
  commit_logger.lua —— 上屏记录器（Rime / librime-lua）
  ------------------------------------------------------------------
  每次文字上屏时，向 Rime 用户文件夹的 commit_log.csv 追加一行：
  时间点、字符数、明文、程序、方案、码长、候选序。

  列契约（写入端固定，下游按此解析）：
    时间点  本地时间，%Y-%m-%d %H:%M:%S
    字符数  上屏明文的 UTF-8 码点数（汉字、字母、标点各自的分类由下游推导）
    明文    原始上屏文本，按 CSV 规则转义（含 , " CR LF 时加引号、内部引号双写）
    程序    client_app，即前台进程名（wechat.exe / code.exe），取不到写空
    方案    schema_id（rime_ice）
    码长    输入串去掉分隔空格后的字符数；纯标点/英文直出无编码时为 0
    候选序  0 = 首选；1..N = 第 N+1 个候选（全局序号）；-1 = 非候选上屏

  候选序取不到 menu 时返回 -1，绝不写空字符串。

  输出（均在 Rime 用户文件夹）：
    commit_log.csv        正式记录，UTF-8 BOM，Excel 可直接双击打开
    commit_log.debug.txt  调试痕迹（只在本文件 CONFIG.debug = true 时生成）
    commit_log.err.txt    出错信息（写盘/取上屏文本失败时生成）

  用法：rime_ice.custom.yaml
        patch:
          "engine/processors/+":
            - lua_processor@*commit_logger
        同时 Rime 用户文件夹需要有 rime.lua 把 * 名称映射到模块。
--]]

local M = {}

local CONFIG = {
  file_name   = "commit_log.csv",
  debug_name  = "commit_log.debug.txt",
  err_name    = "commit_log.err.txt",
  debug       = false,                     -- 排查问题时改成 true(会生成 commit_log.debug.txt)
  with_bom    = true,
  header      = { "时间点", "字符数", "明文", "程序", "方案", "码长", "候选序" },
  time_fmt    = "%Y-%m-%d %H:%M:%S",
  min_chars   = 1,
  max_pending = 5000,
  eol         = "\r\n",
}

M.CONFIG = CONFIG

------------------------------------------------------------------
-- 基础工具
------------------------------------------------------------------

local function normalize(path)
  return (path:gsub("\\", "/"))
end

local function utf8_len(s)
  if utf8 and utf8.len then
    local ok, n = pcall(utf8.len, s)
    if ok and n then return n end
  end
  local _, cnt = s:gsub("[^\128-\191]", "")
  return cnt
end

local function csv_field(v)
  v = tostring(v)
  if v:find('[",\r\n]') then
    return '"' .. (v:gsub('"', '""')) .. '"'
  end
  return v
end

------------------------------------------------------------------
-- 定位 Rime 用户文件夹（结果缓存）
------------------------------------------------------------------

local dir_cache = nil

local function user_dir()
  if dir_cache then return dir_cache end

  local ok, api = pcall(require, "rime_api")
  if ok and type(api) == "table" then
    for _, key in ipairs({ "get_user_data_dir", "get_user_data_sync_dir" }) do
      local fn = api[key]
      if type(fn) == "function" then
        local ok2, d = pcall(fn)
        if ok2 and type(d) == "string" and d ~= "" then
          dir_cache = d
          return dir_cache
        end
      end
    end
  end

  local appdata = os.getenv("APPDATA")
  if appdata and appdata ~= "" then
    dir_cache = appdata .. "\\Rime"
    return dir_cache
  end

  local home = os.getenv("HOME")
  if home and home ~= "" then
    dir_cache = home .. "/Library/Rime"
    return dir_cache
  end

  dir_cache = "."
  return dir_cache
end

local function path_of(name)
  return normalize(user_dir()) .. "/" .. name
end

local function append_file(name, text)
  local f = io.open(path_of(name), "ab")
  if not f then return false end
  f:write(text)
  f:flush()
  f:close()
  return true
end

-- 调试痕迹：只写文件，不抛错
local function trace(msg)
  if not CONFIG.debug then return end
  pcall(append_file, CONFIG.debug_name,
        os.date(CONFIG.time_fmt) .. "\t" .. tostring(msg) .. CONFIG.eol)
end

local function trace_err(msg)
  pcall(append_file, CONFIG.err_name,
        os.date(CONFIG.time_fmt) .. "\t" .. tostring(msg) .. CONFIG.eol)
end

M.trace = trace

------------------------------------------------------------------
-- CSV 写入（带失败重试队列）
------------------------------------------------------------------

local state = {
  header_ok = false,
  pending   = {},
}

local function file_has_content(path)
  local f = io.open(path, "rb")
  if not f then return false end
  local sig = f:read(1)
  f:close()
  return sig ~= nil
end

local function write_header(path)
  local f = io.open(path, "wb")
  if not f then return false end
  local ok = pcall(function()
    if CONFIG.with_bom then f:write("\239\187\191") end
    f:write(table.concat(CONFIG.header, ",") .. CONFIG.eol)
  end)
  f:flush()
  f:close()
  return ok
end

local function flush()
  if #state.pending == 0 then return true end

  local path = path_of(CONFIG.file_name)
  if not state.header_ok then
    if not file_has_content(path) and not write_header(path) then
      trace_err("write_header failed: " .. path)
      return false
    end
    state.header_ok = true
  end

  local f = io.open(path, "ab")
  if not f then
    trace_err("open for append failed: " .. path)
    return false
  end

  local ok, err = pcall(function()
    for i = 1, #state.pending do
      f:write(state.pending[i], CONFIG.eol)
    end
  end)
  pcall(function() f:flush() end)
  f:close()

  if ok then
    state.pending = {}
    return true
  end

  trace_err("write failed: " .. tostring(err))
  return false
end

------------------------------------------------------------------
-- 记录一条上屏
------------------------------------------------------------------

-- 前台程序名：Weasel 在建立会话时把客户端进程名写进 context 属性
-- client_app（就是 weasel.yaml 里 app_options 用的那个 "xxx.exe"）。
-- 取不到就退回 client_type，再取不到就留空。
local function client_app(ctx)
  local ok, v = pcall(function() return ctx:get_property("client_app") end)
  if ok and type(v) == "string" and v ~= "" then return v end

  ok, v = pcall(function() return ctx:get_property("client_type") end)
  if ok and type(v) == "string" then return v end

  return ""
end

-- 当前方案 id（用户切方案时这里会跟着变，所以在提交时读，不在 init 时缓存）
local function schema_id(env)
  if not env or not env.engine then return "" end
  local ok, id = pcall(function() return env.engine.schema.schema_id end)
  if ok and type(id) == "string" then return id end
  return ""
end

-- 码长：输入串里真正参与编码的字符数。
-- 去掉分隔空格，再只数 ASCII 字母/数字——纯标点上屏时 input 里是那个标点
-- 本身（不是编码），按契约应当记 0，而不是 1。
-- 第二个返回值是“归一化输入串”，用来和候选快照配对（上下屏时 input 可能
-- 多一个分隔空格，必须归一化后才能对上）。
local function code_len(ctx)
  local ok, input = pcall(function() return ctx.input end)
  if not ok or type(input) ~= "string" then return 0, "" end
  local stripped = input:gsub("%s+", "")
  local alnum = stripped:gsub("[^%w]", "")
  return utf8_len(alnum), stripped
end

-- 读 menu 的选中序号（0 起）。有的版本把它暴露成字段，有的暴露成方法，
-- 两种都试一遍；若该版本同时给了 page_no / page_size，就换算成全局序号
-- （si 是全局号时 si % ps 仍是页内号，si 是页内号时直接可用，两种语义都对）。
local function menu_index(menu)
  if not menu then return nil end

  local si
  local ok, v = pcall(function() return menu.selected_index end)
  if ok then
    if type(v) == "number" then
      si = v
    elseif type(v) == "function" then
      local ok2, r = pcall(v, menu)
      if ok2 and type(r) == "number" then si = r end
    end
  end
  if not si then return nil end

  local okp, ps = pcall(function() return menu.page_size end)
  local okn, pn = pcall(function() return menu.page_no end)
  if okp and okn and type(ps) == "number" and type(pn) == "number" and ps > 0 then
    return pn * ps + (si % ps)
  end

  return si
end

-- 枚举候选反查序号：menu 取不到时的后备。
-- 部分版本 Context 直接暴露 get_candidate_at(i)（下标是全局序号）。
local function enum_index(ctx, target)
  if type(target) ~= "string" then return nil end
  for i = 0, 199 do
    local ok, c = pcall(function() return ctx:get_candidate_at(i) end)
    if not ok or c == nil then break end
    if c.text == target then return i end
  end
  return nil
end

-- 统一解析“当前高亮是第几个候选”
local function resolve_index(ctx)
  local ok, menu = pcall(function() return ctx.menu end)
  if ok then
    local idx = menu_index(menu)
    if idx then return idx, "menu" end
  end

  local okc, cand = pcall(function() return ctx:get_selected_candidate() end)
  if okc and cand then
    local i = enum_index(ctx, cand.text)
    if i then return i, "enum" end
  end

  return nil, "none"
end

-- 候选序号：0 = 首选，1..N = 第 N+1 个候选（全局序号），-1 = 非候选上屏。
-- 判定不看时间窗，只看“输入串 + 候选快照”能不能对上，这样打完停几秒
-- 再上屏也不会丢序号。
local function candidate_index(ctx, text, st, input_now)
  local idx, src = resolve_index(ctx)
  if idx then return idx, src end

  local map = M.cand_map
  if map then
    -- 首选：快照的输入串与本次上屏的输入串一致
    if input_now and map.__input == input_now then
      local hit = map[text]
      if type(hit) == "number" then return hit, "filter" end
    end

    -- 次选：输入串对不上（例如被别的窗口覆盖过），但快照还热且确实含有这个词
    if (os.time() - (M.cand_map_at or 0)) <= 15 then
      local hit = map[text]
      if type(hit) == "number" then return hit, "filter*" end
    end
  end

  if st and st.text == text and type(st.idx) == "number" then
    return st.idx, "sel"
  end

  return -1, "none"
end

-- 按键瞬间的候选快照。
-- menu 在上屏确认后就被清掉，所以只能在按空格/数字键的那一刻先把
-- “高亮是谁、排第几”记下来，提交时再按明文匹配取用。
local press = { text = nil, idx = nil, map = nil, at = 0 }

local function record(ctx, env, st)
  if not ctx then return end

  local ok, text = pcall(function() return ctx:get_commit_text() end)
  if not ok then
    trace_err("get_commit_text: " .. tostring(text))
    return
  end
  if type(text) ~= "string" or text == "" then return end

  -- 字符数固定为 UTF-8 码点数，汉字/字母/标点由下游自行推导
  local n = utf8_len(text)
  if n < CONFIG.min_chars then return end

  local app = client_app(ctx)
  local sid = schema_id(env)
  local clen, input_now = code_len(ctx)

  -- 两条判定（不再看时间窗）：
  --   码长 0（纯标点/无编码）→ -1
  --   其余 → 用“输入串 + 候选快照”查序号，查不到才 -1
  local cidx, cfrom
  if clen <= 0 then
    cidx, cfrom = -1, "punct"
  else
    cidx, cfrom = candidate_index(ctx, text, st, input_now)
    if cidx < 0 and press.text == text and type(press.idx) == "number" then
      cidx, cfrom = press.idx, "key"
    end
  end

  -- 正常中文却打了 -1，才记诊断
  if clen > 0 and cidx < 0 then
    local okm, menu = pcall(function() return ctx.menu end)
    local mtype = "err"
    local stype = "n/a"
    if okm then
      mtype = (menu == nil) and "nil" or type(menu)
      if menu ~= nil then
        local oks, v = pcall(function() return menu.selected_index end)
        stype = oks and type(v) or "err"
      end
    end
    local okc, cand = pcall(function() return ctx:get_selected_candidate() end)
    local ctype = "err"
    if okc then ctype = (cand == nil) and "nil" or type(cand) end
    trace("diag: miss input=[" .. tostring(input_now) .. "] menu=" .. mtype ..
          " selected_index=" .. stype .. " cand=" .. ctype)
  end

  local line = table.concat({
    csv_field(os.date(CONFIG.time_fmt)),
    csv_field(n),
    csv_field(text),
    csv_field(app),
    csv_field(sid),
    csv_field(clen),
    csv_field(cidx),
  }, ",")

  local q = state.pending
  q[#q + 1] = line
  if #q > CONFIG.max_pending then
    table.remove(q, 1)
  end

  trace("commit: " .. text .. " (" .. n .. ") app=" .. (app ~= "" and app or "-") ..
        " schema=" .. (sid ~= "" and sid or "-") ..
        " len=" .. clen .. " idx=" .. tostring(cidx) .. "(" .. cfrom .. ")")
  flush()
end

M.record = record
M.flush = flush

-- 供 commit_logger_filter.lua 调用：记录「明文 → 全局候选序号」快照
M.cand_map = nil
M.cand_map_at = 0
function M.note_map(map)
  M.cand_map = map
  M.cand_map_at = os.time()
end

------------------------------------------------------------------
-- Processor 接口
------------------------------------------------------------------

-- 每个引擎（每个前端会话/窗口）都有自己的 Context，必须各连各的。
-- 之前的写法是"全局只留一个连接，新的把旧的断开"，结果在别的窗口一打字，
-- 原来那个窗口的连接就被抢走了，于是再也记录不到 —— 这正是 19:24 之后
-- 日志只剩 init、没有 commit 的原因。
M.conns = M.conns or {}

local function connect(env)
  local ctx = env and env.engine and env.engine.context
  if not ctx then
    trace_err("init: no context")
    return nil
  end

  -- 每个引擎一份快照：选词瞬间记下 {候选文本, 序号}，上屏时用来补候选序
  local st = { text = nil, idx = nil }

  local ok, conn = pcall(function()
    return ctx.commit_notifier:connect(function(c)
      local ok2, err = pcall(record, c, env, st)
      if not ok2 then trace_err("record: " .. tostring(err)) end
    end)
  end)
  if not ok then
    trace_err("connect: " .. tostring(conn))
    return nil
  end

  local conns = { conn }

  local ok3, sel = pcall(function()
    return ctx.select_notifier:connect(function(c)
      pcall(function()
        local cand = c:get_selected_candidate()
        if not cand then return end
        local idx = menu_index(c.menu)
        if not idx then idx = enum_index(c, cand.text) end
        if not idx then return end
        st.text = cand.text
        st.idx = idx
      end)
    end)
  end)
  if ok3 and sel then
    conns[#conns + 1] = sel
  else
    trace_err("select_notifier: " .. tostring(sel))
  end

  return conns
end

function M.init(env)
  local conns = connect(env)
  if not conns then
    trace_err("init: connect returned nil")
    return
  end
  for i = 1, #conns do
    M.conns[#M.conns + 1] = conns[i] -- 保住引用，否则被 GC 后连接会自动断开
  end
  trace("engine connected (total " .. #M.conns .. ")")
end

-- 按键阶段抓候选状态：本处理器排在 key_binder / speller / selector /
-- express_editor 之前，所以空格和数字键都会先经过这里。
local function capture_key(key, env)
  if not key then return end

  local okr, rep = pcall(function() return key:repr() end)
  if not okr or type(rep) ~= "string" then return end

  local is_space = (rep == "space")
  local is_digit = (#rep == 1 and rep >= "1" and rep <= "9")
  if not is_space and not is_digit then return end

  local ctx = env and env.engine and env.engine.context
  if not ctx then return end

  local okc, composing = pcall(function() return ctx:is_composing() end)
  if not okc or not composing then return end

  -- 记住“刚刚按了选择键”，提交时用它区分候选上屏和非候选上屏
  press.at = os.time()
  press.text = nil
  press.idx = nil

  if is_space then
    local idx, src = resolve_index(ctx)
    local okg, cand = pcall(function() return ctx:get_selected_candidate() end)
    if idx and okg and cand then
      press.text = cand.text
      press.idx = idx
      if CONFIG.debug then
        trace("captured space idx=" .. idx .. "(" .. src .. ") text=" .. tostring(cand.text))
      end
    elseif CONFIG.debug then
      local okh, has_menu = pcall(function() return ctx:has_menu() end)
      trace("space no index: has_menu=" .. tostring(okh and has_menu) ..
            " cand=" .. tostring(okg and cand ~= nil))
    end
  end
end

-- 不在 func 里补连接：init 每个引擎只走一次，一处连接就够，也避免重复记录。
function M.func(key, env)
  pcall(capture_key, key, env)
  if #state.pending > 0 then pcall(flush) end
  return 2 -- kNoop
end

-- 模块被 require 时留一条痕迹，用于确认 rime.lua 能正常加载本模块
trace("module loaded, dir=" .. user_dir())

return M
