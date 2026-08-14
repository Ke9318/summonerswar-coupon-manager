using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SWCouponManager;

public sealed class MainForm : Form
{
    private const string CouponUrl = "https://event.withhive.com/ci/smon/evt_coupon";

    private readonly AppStorage _storage = new();
    private readonly CouponSourceService _sources = new();
    private readonly GitHubUpdateService _updates = new();
    private AppState _state;

    private readonly DataGridView _accounts = new();
    private readonly ListBox _codes = new();
    private readonly ListBox _results = new();
    private readonly Label _status = new();
    private readonly Button _scan = new() { Text = "스캔" };
    private readonly Button _runNew = new() { Text = "새 쿠폰 등록" };
    private readonly Button _runAll = new() { Text = "전체 검사" };
    private readonly Button _stop = new() { Text = "작업 중지", Enabled = false };
    private readonly Button _addAccount = new() { Text = "계정 추가" };
    private readonly Button _deleteAccount = new() { Text = "선택 삭제" };
    private readonly Button _update = new() { Text = "업데이트 적용", Visible = false };
    private readonly Label _version = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private readonly WebView2 _web = new();

    private UpdateInfo? _availableUpdate;
    private CancellationTokenSource? _workCts;

    public MainForm()
    {
        _state = _storage.Load();

        Text = "Summoners War 쿠폰 매니저";
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(680, 560);

        if (_state.WindowX >= 0 && _state.WindowY >= 0)
            Location = new Point(_state.WindowX, _state.WindowY);
        else
            StartPosition = FormStartPosition.CenterScreen;

        Size = new Size(Math.Max(680, _state.WindowW), Math.Max(560, _state.WindowH));

        BuildUi();
        _version.Text = $"현재 버전 v{_updates.CurrentVersion.ToString(3)}";
        LoadAccountsToGrid();
        LoadCodesToUi();

        Shown += async (_, _) =>
        {
            try
            {
                await EnsureWebViewAsync();
                await CheckUpdateAsync();
                if (_state.LastScanAt is null || DateTimeOffset.Now - _state.LastScanAt > TimeSpan.FromMinutes(5))
                    await ScanAsync();
            }
            catch (Exception ex)
            {
                SetStatus("초기화 실패: " + ex.Message);
            }
        };

        FormClosing += (_, _) =>
        {
            SaveGridToState();
            _state.WindowX = Location.X;
            _state.WindowY = Location.Y;
            _state.WindowW = Width;
            _state.WindowH = Height;
            _storage.Save(_state);
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        foreach (var b in new[] { _scan, _runNew, _runAll, _stop, _update }) top.Controls.Add(b);
        top.Controls.Add(_version);
        root.Controls.Add(top, 0, 0);
        root.SetColumnSpan(top, 2);

        _accounts.Dock = DockStyle.Fill;
        _accounts.AllowUserToAddRows = false;
        _accounts.RowHeadersVisible = false;
        _accounts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _accounts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _accounts.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "사용", FillWeight = 18 });
        _accounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "닉네임", FillWeight = 38 });
        _accounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "HiveId", HeaderText = "Hive ID", FillWeight = 44 });
        root.Controls.Add(_accounts, 0, 1);

        _codes.Dock = DockStyle.Fill;
        root.Controls.Add(_codes, 1, 1);

        var accountButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        accountButtons.Controls.Add(_addAccount);
        accountButtons.Controls.Add(_deleteAccount);
        root.Controls.Add(accountButtons, 0, 2);

        var codeTitle = new Label { Text = "현재 활성/후보 쿠폰", AutoSize = true, Anchor = AnchorStyles.Left };
        root.Controls.Add(codeTitle, 1, 2);

        _results.Dock = DockStyle.Fill;
        root.Controls.Add(_results, 0, 3);
        root.SetColumnSpan(_results, 2);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_status, 0, 4);
        root.SetColumnSpan(_status, 2);

        Controls.Add(root);

        // WebView2는 자동 등록 작업용. 화면에는 노출하지 않되 초기화 가능한 최소 크기로 유지.
        _web.Width = 2;
        _web.Height = 2;
        _web.Left = -20;
        _web.Top = -20;
        Controls.Add(_web);

        _scan.Click += async (_, _) => await ScanAsync();
        _runNew.Click += async (_, _) => await RunAsync(false);
        _runAll.Click += async (_, _) => await RunAsync(true);
        _stop.Click += (_, _) => _workCts?.Cancel();
        _addAccount.Click += (_, _) => AddAccountRow();
        _deleteAccount.Click += (_, _) => DeleteSelectedRows();
        _update.Click += async (_, _) =>
        {
            if (_availableUpdate is not null)
                await _updates.DownloadAndRestartAsync(_availableUpdate, SetStatus);
        };
    }

    private async Task EnsureWebViewAsync()
    {
        if (_web.CoreWebView2 is not null) return;
        await _web.EnsureCoreWebView2Async();
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException(
            "WebView2 Runtime을 초기화하지 못했습니다. Microsoft Edge WebView2 Runtime을 설치해 주세요.");
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
    }

    private void LoadAccountsToGrid()
    {
        _accounts.Rows.Clear();
        foreach (var a in _state.Accounts)
        {
            var idx = _accounts.Rows.Add(a.Selected, a.Name, a.HiveId);
            _accounts.Rows[idx].Tag = a.Id;
        }
    }

    private void SaveGridToState()
    {
        var list = new List<Account>();
        foreach (DataGridViewRow row in _accounts.Rows)
        {
            var id = row.Tag as string ?? Guid.NewGuid().ToString("N");
            var name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";
            var hive = Convert.ToString(row.Cells["HiveId"].Value)?.Trim() ?? "";
            var selected = Convert.ToBoolean(row.Cells["Use"].Value ?? true);
            if (name.Length == 0 && hive.Length == 0) continue;

            list.Add(new Account
            {
                Id = id,
                Name = name,
                HiveId = hive,
                Selected = selected,
                Server = "korea"
            });
        }

        _state.Accounts = list;
        _storage.Save(_state);
    }

    private void AddAccountRow()
    {
        var idx = _accounts.Rows.Add(true, "", "");
        _accounts.Rows[idx].Tag = Guid.NewGuid().ToString("N");
        _accounts.CurrentCell = _accounts.Rows[idx].Cells["Name"];
        _accounts.BeginEdit(true);
    }

    private void DeleteSelectedRows()
    {
        foreach (DataGridViewRow row in _accounts.SelectedRows.Cast<DataGridViewRow>().ToList())
        {
            if (row.Tag is string id)
                _state.History.Remove(id);
            _accounts.Rows.Remove(row);
        }
        SaveGridToState();
    }

    private async Task<bool> ScanAsync()
    {
        try
        {
            ToggleWorking(true);
            SetStatus("SWGT + 보조 소스 스캔 중...");
            var result = await _sources.ScanAsync();

            _state.LastScanCodes = result.Codes;
            _state.CodeSources = result.Sources;
            _state.LastScanAt = DateTimeOffset.Now;
            _storage.Save(_state);
            LoadCodesToUi();

            SetStatus(result.Errors.Count == 0
                ? $"스캔 완료 · {result.Codes.Count}개 · {string.Join(" + ", result.SuccessfulSources)}"
                : $"스캔 완료 · {result.Codes.Count}개 · {string.Join(" + ", result.SuccessfulSources)} · 일부 실패: {string.Join(" / ", result.Errors)}");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("스캔 실패: " + ex.Message);
            return false;
        }
        finally
        {
            ToggleWorking(false);
        }
    }

    private void LoadCodesToUi()
    {
        _codes.Items.Clear();
        foreach (var code in _state.LastScanCodes)
        {
            var sources = _state.CodeSources.TryGetValue(code, out var s) ? string.Join("+", s) : "?";
            _codes.Items.Add($"{code}  [{sources}]");
        }
    }

    private bool IsDone(string accountId, string code)
    {
        if (!_state.History.TryGetValue(accountId, out var perAccount)) return false;
        if (!perAccount.TryGetValue(code, out var rec)) return false;
        return rec.Status is "success" or "already" or "expired" or "invalid";
    }

    private async Task RunAsync(bool runAll)
    {
        if (_workCts is not null)
        {
            MessageBox.Show("이미 작업 중입니다.");
            return;
        }

        SaveGridToState();
        if (!await ScanAsync())
            return;

        var selected = _state.Accounts.Where(a => a.Selected && a.HiveId.Length > 0).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("사용할 계정을 하나 이상 선택해 주세요.");
            return;
        }

        var queue = new List<WorkItem>();
        foreach (var a in selected)
        foreach (var c in _state.LastScanCodes)
            if (runAll || !IsDone(a.Id, c))
                queue.Add(new WorkItem(a, c));

        if (queue.Count == 0)
        {
            SetStatus("새로 처리할 쿠폰이 없습니다.");
            return;
        }

        _workCts = new CancellationTokenSource();
        ToggleWorking(true);
        _results.Items.Clear();

        try
        {
            await EnsureWebViewAsync();

            for (var i = 0; i < queue.Count; i++)
            {
                _workCts.Token.ThrowIfCancellationRequested();
                var item = queue[i];
                SetStatus($"{i + 1}/{queue.Count} · {item.Account.Name} · {item.Code}");

                var (status, message) = await RedeemAsync(item, _workCts.Token);
                Record(item, status, message);

                _results.Items.Insert(0, $"{item.Account.Name} · {item.Code} · {DisplayStatus(status)}");
                _storage.Save(_state);
            }

            SetStatus("전체 작업 완료");
        }
        catch (OperationCanceledException)
        {
            SetStatus("작업이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            SetStatus("작업 중지: " + ex.Message);
        }
        finally
        {
            _workCts.Dispose();
            _workCts = null;
            ToggleWorking(false);
        }
    }

    private async Task<(string status, string message)> RedeemAsync(WorkItem item, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Nav(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
        _web.NavigationCompleted += Nav;
        try
        {
            _web.Source = new Uri(CouponUrl);
            using var navigationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            navigationCts.CancelAfter(TimeSpan.FromSeconds(20));
            using var reg = navigationCts.Token.Register(() => tcs.TrySetCanceled(navigationCts.Token));
            var ok = await tcs.Task;
            if (!ok) throw new InvalidOperationException("공식 쿠폰 페이지를 열지 못했습니다.");
        }
        finally
        {
            _web.NavigationCompleted -= Nav;
        }

        var formReady = false;
        for (var i = 0; i < 25; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ready = await _web.ExecuteScriptAsync("""
            Boolean(
              (document.querySelector('#EVTid') || document.querySelector('input[placeholder*="Hive ID"]')) &&
              (document.querySelector('#EVTcode') || document.querySelector('input[placeholder*="쿠폰 코드"]') || document.querySelector('input[placeholder*="Coupon" i]')) &&
              document.querySelector('#EVTselect') &&
              document.querySelector('button.btn_use[data-target="#EVTpop_coupon"]')
            )
            """);
            if (string.Equals(ready, "true", StringComparison.OrdinalIgnoreCase))
            {
                formReady = true;
                break;
            }
            await Task.Delay(200, ct);
        }

        if (!formReady)
            return ("error", "공식 쿠폰 페이지의 서버/Hive ID/쿠폰 코드/사용 버튼을 찾지 못했습니다.");

        var accountJson = JsonSerializer.Serialize(item.Account.HiveId);
        var codeJson = JsonSerializer.Serialize(item.Code);

        var fillScript = $$"""
        (() => {
          const hive = document.querySelector('#EVTid') ||
                       document.querySelector('input[placeholder*="Hive ID"]');
          const coupon = document.querySelector('#EVTcode') ||
                         document.querySelector('input[placeholder*="쿠폰 코드"]') ||
                         document.querySelector('input[placeholder*="Coupon" i]');
          const sel = document.querySelector('#EVTselect');
          const btn = document.querySelector('button.btn_use[data-target="#EVTpop_coupon"]');

          if (!hive || !coupon || !sel || !btn) return JSON.stringify({ok:false,reason:'폼 요소를 찾지 못했습니다.'});

          const ko = [...sel.options].find(o => /한국|korea/i.test(o.textContent || ''));
          if (!ko) return JSON.stringify({ok:false,reason:'한국 서버 옵션을 찾지 못했습니다.'});
          sel.value = ko.value;
          sel.dispatchEvent(new Event('input',{bubbles:true}));
          sel.dispatchEvent(new Event('change',{bubbles:true}));
          try {
            const jq = window.jQuery || window.$;
            if (jq) {
              const $sel = jq(sel);
              if (typeof $sel.selectmenu === 'function') $sel.selectmenu('refresh');
              $sel.trigger('change');
              $sel.trigger('selectmenuchange');
            }
          } catch {}

          const setValue = (el, val) => {
            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')?.set;
            setter ? setter.call(el,val) : el.value = val;
            el.dispatchEvent(new Event('input',{bubbles:true}));
            el.dispatchEvent(new Event('change',{bubbles:true}));
          };

          setValue(hive, {{accountJson}});
          setValue(coupon, {{codeJson}});
          setTimeout(() => btn.click(), 400);
          return JSON.stringify({ok:true});
        })();
        """;

        var fillResult = UnwrapJsString(await _web.ExecuteScriptAsync(fillScript));
        if (!fillResult.Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase))
            return ("error", fillResult);

        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(300, ct);

            var poll = await _web.ExecuteScriptAsync("""
            (() => {
              const els = [...document.querySelectorAll(
                '[role="dialog"],.modal,.popup,.layer,.alert,.pop_wrap,.popup_wrap,#EVTpop_coupon'
              )].filter(e => {
                const s = getComputedStyle(e);
                const r = e.getBoundingClientRect();
                return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
              });
              const txt = els.map(e => e.innerText || e.textContent || '').join('\n').trim();
              return txt;
            })();
            """);

            var message = UnwrapJsString(poll).Trim();
            if (message.Length < 2) continue;
            if (!Regex.IsMatch(message, "쿠폰|coupon|사용|used|만료|expired|성공|success|보상|reward|유효|invalid|오류|error", RegexOptions.IgnoreCase))
                continue;

            var status = Classify(message);
            if (status != "unknown")
                return (status, message);
        }

        return ("error", "결과창을 제한 시간 안에 인식하지 못했습니다.");
    }

    private void Record(WorkItem item, string status, string message)
    {
        if (!_state.History.TryGetValue(item.Account.Id, out var per))
            _state.History[item.Account.Id] = per = new(StringComparer.OrdinalIgnoreCase);

        per[item.Code] = new CouponRecord
        {
            Status = status,
            Message = message,
            Time = DateTimeOffset.Now
        };
    }

    private static string Classify(string message)
    {
        var m = message.ToLowerInvariant();

        if (Regex.IsMatch(m, "already|used|이미\\s*사용|사용한|등록된")) return "already";
        if (Regex.IsMatch(m, "expired|만료")) return "expired";
        if (Regex.IsMatch(m, "success|complete|reward|성공|완료|보상|지급")) return "success";
        if (Regex.IsMatch(m, "invalid|not valid|유효하지|존재하지|wrong|잘못된|없는 쿠폰")) return "invalid";
        if (Regex.IsMatch(m, "error|오류|fail|실패")) return "error";
        return "unknown";
    }

    private static string DisplayStatus(string status) => status switch
    {
        "success" => "성공",
        "already" => "이미 사용",
        "expired" => "만료",
        "invalid" => "무효",
        "error" => "오류",
        _ => status
    };

    private static string UnwrapJsString(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(json) ?? json;
        }
        catch
        {
            return json.Trim('"').Replace("\\n", "\n").Replace("\\\"", "\"");
        }
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            SetStatus("업데이트 확인 중...");
            _availableUpdate = await _updates.CheckAsync();

            if (_availableUpdate is null)
            {
                SetStatus($"최신 버전 · v{_updates.CurrentVersion.ToString(3)}");
                return;
            }

            _update.Visible = true;
            _update.Text = $"v{_availableUpdate.Version} 업데이트";
            SetStatus($"새 버전 {_availableUpdate.Tag} 사용 가능");
        }
        catch
        {
            SetStatus("업데이트 확인 실패 · 프로그램은 그대로 사용할 수 있습니다.");
        }
    }

    private void ToggleWorking(bool busy)
    {
        _scan.Enabled = !busy;
        _runNew.Enabled = !busy;
        _runAll.Enabled = !busy;
        _addAccount.Enabled = !busy;
        _deleteAccount.Enabled = !busy;
        _stop.Enabled = busy && _workCts is not null;
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired) BeginInvoke(() => _status.Text = text);
        else _status.Text = text;
    }
}
