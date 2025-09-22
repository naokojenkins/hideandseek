using System;
using System.Net;
using System.Text;
using System.Threading;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.IO;

namespace ToolUse.Sim.Application
{
    /// <summary>
    /// Very small in-process HTTP server serving a live dashboard and JSON metrics.
    /// Uses HttpListener to avoid ASP.NET Core dependency. Intended for local access only (localhost).
    /// </summary>
    public sealed class DashboardServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Thread _thread;
        private volatile bool _running;
        private readonly string _prefix;
        private readonly MetricsRecorder _metrics = MetricsRecorder.Instance;

        public DashboardServer(int port)
        {
            _prefix = $"http://localhost:{port}/";
            _listener.Prefixes.Add(_prefix);
            try { _listener.Prefixes.Add($"http://127.0.0.1:{port}/"); } catch { }
            try { _listener.IgnoreWriteExceptions = true; } catch { }
            _thread = new Thread(ListenLoop) { IsBackground = true, Name = "DashboardServer" };
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _running = true;
                _thread.Start();
                Console.WriteLine($"[Dashboard] Listening at {_prefix} (open in browser)");
            }
            catch (HttpListenerException ex)
            {
                Console.Error.WriteLine($"[Dashboard] Failed to start listener at {_prefix}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Dashboard] Error starting: {ex.Message}");
            }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext? ctx = null;
                try { ctx = _listener.GetContext(); }
                catch { if (!_running) break; else continue; }
                if (ctx == null) continue;

                try
                {
                    var req = ctx.Request;
                    var resp = ctx.Response;
                    string path = req.Url?.AbsolutePath?.ToLowerInvariant() ?? "/";

                    if (path == "/" || path == "/dashboard")
                    {
                        RespondHtml(resp, BuildHtmlPage());
                    }
                    else if (path == "/metrics.json")
                    {
                        var snap = _metrics.GetWebSnapshot();
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = null, WriteIndented = false };
                        try { jsonOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull; } catch { }
                        jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                        string json = System.Text.Json.JsonSerializer.Serialize(snap, jsonOptions);
                        try { Console.WriteLine($"[Dashboard] /metrics.json: train={snap.TrainingSeries?.Count ?? 0}, episode={snap.EpisodeSeries?.Count ?? 0}, step={snap.LatestTraining?.Step}"); } catch { }
                        RespondJson(resp, json);
                    }
                    else if (path == "/metrics.txt")
                    {
                        var snap = _metrics.GetWebSnapshot();
                        string text = $"now={snap.Now}\nstep={snap.LatestTraining?.Step}\nepsilon={snap.LatestTraining?.Epsilon:F3}\nbeta={snap.LatestTraining?.Beta:F3}\nbuffer={snap.LatestTraining?.Buffer}\nema_loss={snap.LatestTraining?.EmaLoss:F4}\ntrain_points={snap.TrainingSeries?.Count}\nepisode_points={snap.EpisodeSeries?.Count}\n";
                        RespondJson(resp, text);
                    }
                    else if (path == "/ping")
                    {
                        RespondJson(resp, "ok");
                    }
                    else if (path == "/debug")
                    {
                        var snap = _metrics.GetWebSnapshot();
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = null, WriteIndented = true };
                        try { jsonOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull; } catch { }
                        jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                        string json = System.Text.Json.JsonSerializer.Serialize(snap, jsonOptions);
                        var html = "<html><body><h3>metrics.json (debug)</h3><pre style='white-space:pre-wrap;word-break:break-word;background:#0d1117;color:#e6edf3;padding:10px;border-radius:8px'>" + System.Net.WebUtility.HtmlEncode(json) + "</pre></body></html>";
                        RespondHtml(resp, html);
                    }
                    else
                    {
                        resp.StatusCode = 404;
                        var msg = Encoding.UTF8.GetBytes("Not found");
                        resp.OutputStream.Write(msg, 0, msg.Length);
                        resp.OutputStream.Close();
                    }
                }
                catch { /* ignore per-request errors */ }
            }
        }

        private static void RespondHtml(HttpListenerResponse resp, string html)
        {
            byte[] buf = Encoding.UTF8.GetBytes(html);
            resp.ContentType = "text/html; charset=utf-8";
            try {
                resp.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
                resp.Headers["Pragma"] = "no-cache";
                resp.Headers["Expires"] = "0";
            } catch { }
            resp.ContentLength64 = buf.Length;
            resp.StatusCode = 200;
            resp.OutputStream.Write(buf, 0, buf.Length);
            resp.OutputStream.Close();
        }

        private static void RespondJson(HttpListenerResponse resp, string json)
        {
            byte[] buf = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json";
            try {
                resp.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
                resp.Headers["Pragma"] = "no-cache";
                resp.Headers["Expires"] = "0";
                resp.Headers["Access-Control-Allow-Origin"] = "*";
            } catch { }
            resp.ContentLength64 = buf.Length;
            resp.StatusCode = 200;
            resp.OutputStream.Write(buf, 0, buf.Length);
            resp.OutputStream.Close();
        }

        private static string BuildHtmlPage()
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" /><title>Hide&Seek — Training Dashboard</title>");
            sb.Append("<style>*{box-sizing:border-box}:root{--vh:1vh}html,body{height:100%;}body{height:calc(var(--vh,1vh)*100);display:flex;flex-direction:column;margin:0;padding:0;background:#0f141a;color:#e6edf3;overflow:hidden}header{flex:0 0 auto;padding:6px 10px;background:#161b22;position:sticky;top:0;z-index:1}.content{flex:1 1 auto;min-height:0;display:grid;grid-template-rows:auto auto 1fr} .grid{display:grid;grid-template-columns:1fr;gap:8px;padding:8px} .charts{display:grid;grid-auto-rows:1fr;gap:8px;height:100%;min-height:0} .card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:8px;min-height:0;overflow:hidden} .card.full{grid-column:1 / -1} .kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:6px} .kpi{background:#0d1117;border:1px solid #30363d;border-radius:8px;padding:6px} .kpi .label{color:#8b949e;font-size:11px} .kpi .value{font-size:16px;font-weight:bold} .chart-wrap{height:100%;min-height:60px;display:block} canvas{background:#0d1117;border-radius:6px;display:block;width:100%!important;height:100%!important} a{color:#58a6ff} /* Ensure chart never overflows bottom at any zoom */ .content,.grid,#chartsArea,.card,.chart-wrap{min-height:0;overflow:hidden}</style>");
            sb.Append("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script></head><body>");
            sb.Append("<header><div><strong>Hide &amp; Seek — DQN Training Dashboard</strong></div><div id=\"meta\" style=\"font-size:12px;color:#8b949e\"></div></header>");
            sb.Append("<div class=\"content\"><div class=\"grid\" style=\"grid-template-rows: auto auto 1fr;\">");
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Episode</strong></div><div class=\"kpis\">");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Sessions</div><div id=\"e_sess\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Last time (s)</div><div id=\"e_time\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Caught</div><div id=\"e_caught\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Visibility ratio</div><div id=\"e_vis\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Avg distance</div><div id=\"e_dist\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Seeker explored (phys/vis/total)</div><div id=\"e_expl\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Acc reward (S/H)</div><div id=\"e_rew\" class=\"value\">-</div></div>");
            sb.Append("</div></div>");
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Resources</strong></div><div class=\"kpis\">");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Step</div><div id=\"k_step\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Epsilon</div><div id=\"k_eps\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Beta (PER)</div><div id=\"k_beta\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Buffer size</div><div id=\"k_buf\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">EMA Loss</div><div id=\"k_loss\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Q mean / max</div><div id=\"k_q\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Device</div><div id=\"k_dev\" class=\"value\">-</div></div>");
            sb.Append("</div></div>");
            sb.Append("<div id=\"chartsArea\" class=\"charts\">");
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Learning Progress</strong></div><div class=\"kpis\">");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Catch rate (last 100)</div><div id=\"lp_catch\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Avg ep time (s, last 100)</div><div id=\"lp_time\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Avg seeker reward (last 100)</div><div id=\"lp_rew\" class=\"value\">-</div></div>");
            sb.Append("<div class=\"kpi\"><div class=\"label\">Steps/min (last 300 pts)</div><div id=\"lp_speed\" class=\"value\">-</div></div>");
            sb.Append("</div><div class=\"chart-wrap\"><canvas id=\"catchChart\"></canvas></div></div>");
            // New additional charts for more visual metrics
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Training Dynamics</strong></div><div class=\"chart-wrap\"><canvas id=\"lossChart\"></canvas></div></div>");
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Epsilon / Beta</strong></div><div class=\"chart-wrap\"><canvas id=\"epsBetaChart\"></canvas></div></div>");
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Replay Buffer Size</strong></div><div class=\"chart-wrap\"><canvas id=\"bufferChart\"></canvas></div></div>");
            sb.Append("<div class=\"card full\"><div style=\"margin-bottom:8px\"><strong>Episode Time & Visibility</strong></div><div class=\"chart-wrap\"><canvas id=\"epTimeVisChart\"></canvas></div></div>");
            sb.Append("</div></div></div>");
            // Full dashboard script (compact)
            sb.Append("<script>\n" +
                      "function setVh(){const vh=window.innerHeight*0.01;document.documentElement.style.setProperty('--vh', vh+'px');}\n" +
                      "setVh();window.addEventListener('resize',()=>{setVh(); try{if(window.charts){for(const k in charts){if(charts[k]&&charts[k].resize)charts[k].resize();}}}catch(e){}});\n" +
                      "const charts = {};\n" +
                      "function makeChart(id,label,color){\n" +
                      "  const ctx=document.getElementById(id).getContext('2d');\n" +
                      "  return new Chart(ctx,{type:'line',data:{labels:[],datasets:[{label:label,data:[],borderColor:color,tension:0.2,pointRadius:0,fill:false}]},options:{responsive:true,maintainAspectRatio:false,animation:false,scales:{x:{display:false},y:{ticks:{color:'#e6edf3'},grid:{color:'#30363d'}}},plugins:{legend:{labels:{color:'#e6edf3'}}}}});\n" +
                      "}\n" +
                      "function setText(id,val){var el=document.getElementById(id); if(el){ el.innerText = val; }}\n" +
                      "function fmtNum(x, d){ if(x===null||x===undefined||isNaN(x)) return '-'; try{ return Number(x).toFixed(d); }catch(e){ return '-'; } }\n" +
                      "function pushData(c,labels,data){ if(!c) return; c.data.labels = labels; c.data.datasets[0].data = data; c.update('none'); c.resize(); }\n" +
                      "function movingAvg(arr,w){ if(!arr||arr.length===0) return []; var out=[],sum=0; for(var i=0;i<arr.length;i++){ sum+=arr[i]; if(i>=w) sum-=arr[i-w]; out.push(i>=w-1? sum/w : null); } return out; }\n" +
                      "async function refresh(){\n" +
                      "  try{\n" +
                      "    const r = await fetch('/metrics.json',{cache:'no-store'});\n" +
                      "    const a = await r.json();\n" +
                      "    var nowTs = (a && a.now) ? a.now : 0;\n" +
                      "    document.getElementById('meta').innerText = 'Now: ' + (nowTs? new Date(nowTs*1000).toLocaleString() : '-') + ' • Device: ' + (a.device||'-') + ' • Logs: ' + (a.logsDir||'-');\n" +
                      "    var t = a.latestTraining || {};\n" +
                      "    var e = a.latestEpisode || {};\n" +
                      "    setText('k_step', (t.step!=null? t.step : '-'));\n" +
                      "    setText('k_eps', fmtNum(t.epsilon,3));\n" +
                      "    setText('k_beta', fmtNum(t.beta,3));\n" +
                      "    setText('k_buf', (t.buffer!=null? t.buffer : '-'));\n" +
                      "    setText('k_loss', fmtNum(t.emaLoss,4));\n" +
                      "    setText('k_q', (fmtNum(t.qMean,3) + ' / ' + fmtNum(t.qMax,3)));\n" +
                      "    setText('k_dev', (a.device||'-'));\n" +
                      "    setText('e_sess', (e.totalSession!=null? e.totalSession : '-'));\n" +
                      "    setText('e_time', fmtNum(e.sessionTime,1));\n" +
                      "    setText('e_caught', (e.caught? 'Yes':'No'));\n" +
                      "    setText('e_vis', fmtNum(e.visibilityRatio,3));\n" +
                      "    setText('e_dist', fmtNum(e.avgDistance,3));\n" +
                      "    setText('e_expl', ((e.seekerPhysical||0) + '/' + (e.seekerVisual||0) + '/' + (e.seekerTotal||0)));\n" +
                      "    setText('e_rew', (fmtNum(e.accSeekerReward,2) + ' / ' + fmtNum(e.accHiderReward,2)));\n" +
                      "    var s = a.trainingSeries || [];\n" +
                      "    var l = a.episodeSeries || [];\n" +
                      "    var xs = s.map(function(o){ return (o && o.step!=null)? o.step : 0; });\n" +
                      "    var eps = s.map(function(o){ return (o && o.epsilon!=null)? o.epsilon : 0; });\n" +
                      "    var epIndex = Array.from({length: l.length}, function(_,i){ return i+1; });\n" +
                      "    // Removed extra charts; keeping only Learning Progress (catch MA)\n" +
                      "    var W=100, EM=300;\n" +
                      "    var caughtArr = l.map(function(o){ return (o && o.caught)? 1:0; });\n" +
                      "    var lastCaught = caughtArr.slice(Math.max(0, caughtArr.length - W));\n" +
                      "    var catchRate = lastCaught.length ? lastCaught.reduce(function(x,y){return x+y;}, 0) / lastCaught.length : 0;\n" +
                      "    setText('lp_catch', (catchRate*100).toFixed(1) + '%');\n" +
                      "    var lastTimes = l.map(function(o){ return (o && o.session_time!=null)? o.session_time : 0; }).slice(Math.max(0, l.length - W));\n" +
                      "    var avgTime = lastTimes.length ? lastTimes.reduce(function(x,y){return x+y;}, 0) / lastTimes.length : 0;\n" +
                      "    setText('lp_time', avgTime.toFixed(1));\n" +
                      "    var lastRew = l.map(function(o){ return (o && o.acc_seeker_reward!=null)? o.acc_seeker_reward : 0; }).slice(Math.max(0, l.length - W));\n" +
                      "    var avgRew = lastRew.length ? lastRew.reduce(function(x,y){return x+y;}, 0) / lastRew.length : 0;\n" +
                      "    setText('lp_rew', avgRew.toFixed(2));\n" +
                      "    var stepsArr = s.map(function(o){ return (o && o.step!=null)? o.step : 0; });\n" +
                      "    var tsArr = s.map(function(o){ return (o && o.ts!=null)? o.ts : 0; });\n" +
                      "    var spm='-';\n" +
                      "    if(stepsArr.length>=2){\n" +
                      "      var start = Math.max(0, stepsArr.length-EM);\n" +
                      "      var ds = stepsArr[stepsArr.length-1] - stepsArr[start];\n" +
                      "      var dtSec = (tsArr[tsArr.length-1] - (tsArr[start]||tsArr[tsArr.length-1])) || 0;\n" +
                      "      spm = dtSec>0 ? String((ds/(dtSec/60)).toFixed(0)) : '-';\n" +
                      "    }\n" +
                      "    setText('lp_speed', spm);\n" +
                      "    var catchTrend = movingAvg(caughtArr, Math.max(5, Math.floor(W/10)));\n" +
                      "    var epIdx = Array.from({length: catchTrend.length}, function(_,i){ return i+1; });\n" +
                      "    (function(){\n" +
                      "      if(charts.catch){\n" +
                      "        var vals = catchTrend.filter(function(v){ return v!=null && !isNaN(v); });\n" +
                      "        var ymin = 0, ymax = 1;\n" +
                      "        if(vals.length > 0){\n" +
                      "          ymin = Math.min.apply(null, vals);\n" +
                      "          ymax = Math.max.apply(null, vals);\n" +
                      "          if (ymax === ymin) { ymax = ymin + 1; }\n" +
                      "          var pad = Math.max(0.05, 0.1 * (ymax - ymin));\n" +
                      "          ymin = Math.max(0, ymin - pad);\n" +
                      "          ymax = Math.min(1, ymax + pad);\n" +
                      "        }\n" +
                      "        if(!charts.catch.options.scales) charts.catch.options.scales = {};\n" +
                      "        if(!charts.catch.options.scales.y) charts.catch.options.scales.y = {};\n" +
                      "        charts.catch.options.scales.y.min = ymin;\n" +
                      "        charts.catch.options.scales.y.max = ymax;\n" +
                      "      }\n" +
                      "    })();\n" +
                      "    pushData(charts.catch, epIdx, catchTrend);\n" +
                      "\n" +
                      "    // Additional charts: Loss over steps\n" +
                      "    var loss = s.map(function(o){ return (o && o.ema_loss!=null)? o.ema_loss : null; });\n" +
                      "    pushData(charts.loss, xs, loss);\n" +
                      "\n" +
                      "    // Epsilon/Beta\n" +
                      "    var beta = s.map(function(o){ return (o && o.beta!=null)? o.beta : null; });\n" +
                      "    if(charts.epsBeta){\n" +
                      "      charts.epsBeta.data.labels = xs;\n" +
                      "      charts.epsBeta.data.datasets[0].data = eps;\n" +
                      "      charts.epsBeta.data.datasets[1].data = beta;\n" +
                      "      charts.epsBeta.update('none'); charts.epsBeta.resize();\n" +
                      "    }\n" +
                      "\n" +
                      "    // Buffer size\n" +
                      "    var buf = s.map(function(o){ return (o && o.buffer!=null)? o.buffer : null; });\n" +
                      "    pushData(charts.buffer, xs, buf);\n" +
                      "\n" +
                      "    // Episode time & visibility\n" +
                      "    var epTime = l.map(function(o){ return (o && o.session_time!=null)? o.session_time : null; });\n" +
                      "    var vis = l.map(function(o){ return (o && o.visibility_ratio!=null)? o.visibility_ratio : null; });\n" +
                      "    if(charts.epTimeVis){\n" +
                      "      charts.epTimeVis.data.labels = epIndex;\n" +
                      "      charts.epTimeVis.data.datasets[0].data = epTime;\n" +
                      "      charts.epTimeVis.data.datasets[1].data = vis;\n" +
                      "      charts.epTimeVis.update('none'); charts.epTimeVis.resize();\n" +
                      "    }\n" +
                      "\n" +
                      "  }catch(err){ console.error('refresh error', err); }\n" +
                      "}\n" +
                      "window.addEventListener('load', function(){\n" +
                      "  charts.catch= makeChart('catchChart','Catch rate (MA)','orange');\n" +
                      "  charts.loss = makeChart('lossChart','EMA Loss','red');\n" +
                      "  // Epsilon/Beta dual dataset chart\n" +
                      "  (function(){\n" +
                      "    var ctx=document.getElementById('epsBetaChart').getContext('2d');\n" +
                      "    charts.epsBeta = new Chart(ctx,{type:'line',data:{labels:[],datasets:[{label:'Epsilon',data:[],borderColor:'#58a6ff',tension:0.2,pointRadius:0,fill:false},{label:'Beta',data:[],borderColor:'#9b59b6',tension:0.2,pointRadius:0,fill:false}]},options:{responsive:true,maintainAspectRatio:false,animation:false,scales:{x:{display:false},y:{min:0,max:1,ticks:{color:'#e6edf3'},grid:{color:'#30363d'}}},plugins:{legend:{labels:{color:'#e6edf3'}}}}});\n" +
                      "  })();\n" +
                      "  charts.buffer = makeChart('bufferChart','Replay buffer size','#2ecc71');\n" +
                      "  // Episode time & visibility dual dataset chart\n" +
                      "  (function(){\n" +
                      "    var ctx=document.getElementById('epTimeVisChart').getContext('2d');\n" +
                      "    charts.epTimeVis = new Chart(ctx,{type:'line',data:{labels:[],datasets:[{label:'Episode time (s)',data:[],borderColor:'#f39c12',tension:0.2,pointRadius:0,fill:false},{label:'Visibility ratio',data:[],borderColor:'#16a085',tension:0.2,pointRadius:0,fill:false}]},options:{responsive:true,maintainAspectRatio:false,animation:false,scales:{x:{display:false},y:{ticks:{color:'#e6edf3'},grid:{color:'#30363d'}}},plugins:{legend:{labels:{color:'#e6edf3'}}}}});\n" +
                      "  })();\n" +
                      "  refresh(); setInterval(refresh, 1000);\n" +
                      "});\n" +
                      "</script>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }
}
