using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SWCouponManager;

public sealed class MainForm : Form
{
    private const string CouponUrl = "https://event.withhive.com/ci/smon/evt_coupon";
    private static readonly ServerChoice[] ServerChoices =
    [
        new("global", "글로벌 서버"),
        new("korea", "한국 서버"),
        new("japan", "일본 서버"),
        new("china", "중국 서버"),
        new("asia", "아시아 서버"),
        new("europe", "유럽 서버")
    ];

    private readonly AppStorage _storage = new();
    private readonly CouponSourceService _sources = new();
    private readonly GitHubUpdateService _updates = new();
    private AppState _state;

    private readonly DataGridView _accounts = new();
    private readonly ListBox _codes = new();
    private readonly ListBox _results = new();
    private readonly ListBox _sourceHealth = new();
    private readonly Label _status = new();
    private readonly Button _scan = new() { Text = "새 쿠폰 찾기" };
    private readonly Button _runNew = new() { Text = "새 쿠폰 받기" };
    private readonly Button _runAll = new() { Text = "모든 쿠폰 다시 확인" };
    private readonly Button _stop = new() { Text = "중지", Enabled = false };
    private readonly Button _addAccount = new() { Text = "+ 계정 추가" };
    private readonly Button _deleteAccount = new() { Text = "선택 삭제" };
    private readonly Button _update = new()
    {
        Text = "업데이트 적용",
        Visible = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 3, 8, 3)
    };
    private readonly Button _history = new() { Text = "기록 보기" };
    private readonly Button _settings = new() { Text = "설정" };
    private readonly GroupBox _couponGroup = new() { Text = "쿠폰 후보", Dock = DockStyle.Fill };
    private readonly Label _version = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly FlowLayoutPanel _advanced = new() { Visible = false, AutoSize = true };
    private readonly ToolTip _codeTip = new();
    private readonly WebView2 _web = new();

    private UpdateInfo? _availableUpdate;
    private CancellationTokenSource? _workCts;
    private bool _loadingAccounts;

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
        _version.Text = $"v{_updates.CurrentVersion.ToString(3)}";
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
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(16)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var title = new Label
        {
            Text = "Summoners War 쿠폰 매니저",
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        root.Controls.Add(title, 0, 0);

        _accounts.Dock = DockStyle.Fill;
        _accounts.AllowUserToAddRows = false;
        _accounts.RowHeadersVisible = false;
        _accounts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _accounts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _accounts.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "사용", FillWeight = 12 });
        _accounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "닉네임", FillWeight = 25 });
        _accounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "HiveId", HeaderText = "Hive ID", FillWeight = 38 });
        _accounts.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Server",
            HeaderText = "서버",
            FillWeight = 25,
            FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(ServerChoice.DisplayName),
            ValueMember = nameof(ServerChoice.Value),
            DataSource = ServerChoices
        });
        _accounts.DataError += (_, e) => e.ThrowException = false;
        _accounts.CellEndEdit += (_, _) => SaveGridToState();
        _accounts.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_accounts.IsCurrentCellDirty)
                _accounts.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _accounts.CellValueChanged += (_, _) =>
        {
            if (_accounts.IsHandleCreated && !_loadingAccounts) SaveGridToState();
        };

        var accountGroup = new GroupBox { Text = "계정 · 체크한 계정에 쿠폰을 받습니다", Dock = DockStyle.Fill };
        accountGroup.Controls.Add(_accounts);
        root.Controls.Add(accountGroup, 0, 1);

        _scan.AutoSize = true;
        _runNew.AutoSize = true;
        _scan.Font = new Font(Font, FontStyle.Bold);
        _runNew.Font = new Font(Font, FontStyle.Bold);
        _scan.Padding = new Padding(10, 5, 10, 5);
        _runNew.Padding = new Padding(10, 5, 10, 5);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };
        actions.Controls.Add(_scan);
        actions.Controls.Add(_runNew);
        actions.Controls.Add(_stop);
        actions.Controls.Add(_update);
        root.Controls.Add(actions, 0, 2);

        _codes.Dock = DockStyle.Fill;
        _codes.HorizontalScrollbar = true;
        _codes.MouseMove += (_, e) =>
        {
            var index = _codes.IndexFromPoint(e.Location);
            if (index < 0 || index >= _state.LastScanCodes.Count) return;
            var code = _state.LastScanCodes[index];
            var sources = _state.CodeSources.TryGetValue(code, out var list) ? string.Join(", ", list) : "알 수 없음";
            _codeTip.SetToolTip(_codes, $"출처: {sources}");
        };
        _couponGroup.Controls.Add(_codes);
        root.Controls.Add(_couponGroup, 0, 3);

        _results.Dock = DockStyle.Fill;
        var resultGroup = new GroupBox { Text = "진행 결과", Dock = DockStyle.Fill };
        resultGroup.Controls.Add(_results);
        root.Controls.Add(resultGroup, 0, 4);

        _advanced.Controls.Add(_addAccount);
        _advanced.Controls.Add(_deleteAccount);
        _advanced.Controls.Add(_runAll);
        var secondary = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        secondary.Controls.Add(_history);
        secondary.Controls.Add(_settings);
        secondary.Controls.Add(_advanced);
        root.Controls.Add(secondary, 0, 5);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _version.Anchor = AnchorStyles.Right;
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_version, 1, 0);
        root.Controls.Add(footer, 0, 6);

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
        _settings.Click += (_, _) => _advanced.Visible = !_advanced.Visible;
        _history.Click += (_, _) => ShowHistory();
        _update.Click += async (_, _) =>
        {
            if (_availableUpdate is not null)
                await _updates.DownloadAndRestartAsync(_availableUpdate, SetStatus);
        };
    }

    private void ShowHistory()
    {
        var lines = _state.Accounts.SelectMany(account =>
            _state.History.TryGetValue(account.Id, out var records)
                ? records.OrderByDescending(x => x.Value.Time)
                    .Select(x =>
                    {
                        var sources = _state.CodeSources.TryGetValue(x.Key, out var foundAt)
                            ? string.Join(", ", foundAt)
                            : "알 수 없음";
                        return $"{account.Name} · {x.Key} · {DisplayStatus(x.Value.Status)} · {x.Value.Time.LocalDateTime:g}\r\n출처: {sources}\r\n{x.Value.Message}";
                    })
                : []);
        var text = string.Join("\r\n\r\n", lines);
        MessageBox.Show(text.Length == 0 ? "아직 쿠폰 처리 기록이 없습니다." : text,
                        "쿠폰 처리 기록", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        _loadingAccounts = true;
        _accounts.Rows.Clear();
        foreach (var a in _state.Accounts)
        {
            var idx = _accounts.Rows.Add(a.Selected, a.Name, a.HiveId, NormalizeServer(a.Server));
            _accounts.Rows[idx].Tag = a.Id;
        }
        _loadingAccounts = false;
    }

    private void SaveGridToState()
    {
        var list = new List<Account>();
        foreach (DataGridViewRow row in _accounts.Rows)
        {
            var id = row.Tag as string ?? Guid.NewGuid().ToString("N");
            var name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";
            var hive = Convert.ToString(row.Cells["HiveId"].Value)?.Trim() ?? "";
            var server = NormalizeServer(Convert.ToString(row.Cells["Server"].Value));
            var selected = Convert.ToBoolean(row.Cells["Use"].Value ?? true);
            if (name.Length == 0 && hive.Length == 0) continue;

            list.Add(new Account
            {
                Id = id,
                Name = name,
                HiveId = hive,
                Selected = selected,
                Server = server
            });
        }

        _state.Accounts = list;
        _storage.Save(_state);
    }

    private void AddAccountRow()
    {
        var idx = _accounts.Rows.Add(true, "", "", "korea");
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
            SetStatus("새 쿠폰을 찾는 중...");
            var seen = _state.SeenCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = await _sources.ScanAsync(_state);
            var newCodes = GetNewCodes(result.Codes, seen);
            WriteScanHealth(result);
            ShowSourceHealth(result);

            _state.LastScanCodes = result.Codes;
            _state.CodeSources = result.Sources;
            _state.SeenCodes = seen.Concat(result.Codes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToList();
            _state.LastScanAt = DateTimeOffset.Now;
            _storage.Save(_state);
            LoadCodesToUi();

            var newCount = newCodes.Count;
            _couponGroup.Text = newCount > 0
                ? $"새 쿠폰 후보 {newCount}개 · 전체 후보 {result.Codes.Count}개"
                : $"쿠폰 후보 {result.Codes.Count}개";

            SetStatus(result.Errors.Count == 0
                ? $"완료 · 새 쿠폰 후보 {newCount}개 · 전체 후보 {result.Codes.Count}개"
                : $"완료 · 새 쿠폰 후보 {newCount}개 · {result.Errors.Count}개 소스 확인 실패");
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

    private void WriteScanHealth(ScanResult result)
    {
        try
        {
            Directory.CreateDirectory(_storage.DataDir);
            File.WriteAllLines(Path.Combine(_storage.DataDir, "scan-health.log"), result.Health.Select(health =>
                $"[{DateTimeOffset.Now:O}] {health.Source} fetch={health.FetchSuccesses}/{health.FetchAttempts} " +
                $"hashes=[{string.Join(",", health.PayloadHashes ?? [])}] bytes={health.PayloadBytes} production={health.ProductionCount} " +
                $"reference={health.ReferenceCount?.ToString() ?? "unavailable"} missing={health.MissingCodes.Count} " +
                $"advertised={health.AdvertisedCount?.ToString() ?? "n/a"} retained={health.RetainedRecentCount} suspicious={health.Suspicious} " +
                $"missingCodes=[{string.Join(",", health.MissingCodes)}] extra=[{string.Join(",", health.ExtraCodes)}] " +
                $"error={health.Error ?? "none"}"));
        }
        catch { }
    }

    private void ShowSourceHealth(ScanResult result)
    {
        _sourceHealth.Items.Clear();
        foreach (var h in result.Health)
            _sourceHealth.Items.Add($"{h.Source} · fetch {h.FetchSuccesses}/{h.FetchAttempts} · advertised {h.AdvertisedCount?.ToString() ?? "—"} · " +
                $"extracted {string.Join("/", h.ResponseCodeCounts ?? [])} · union {h.ProductionCount} · retained {h.RetainedRecentCount} · " +
                (h.Suspicious ? "WARNING " + string.Join(", ", h.Warnings ?? []) : "Healthy"));
        using var dialog = new Form { Text = "쿠폰 소스 상태", Width = 900, Height = 280, StartPosition = FormStartPosition.CenterParent };
        _sourceHealth.Dock = DockStyle.Fill;
        dialog.Controls.Add(_sourceHealth);
        dialog.ShowDialog(this);
    }

    private void LoadCodesToUi()
    {
        _codes.Items.Clear();
        foreach (var code in _state.LastScanCodes)
            _codes.Items.Add(code);
        _couponGroup.Text = $"쿠폰 후보 {_state.LastScanCodes.Count}개";
    }

    private bool IsDone(string accountId, string code)
        => !ShouldProcess(_state.History, accountId, code);

    internal static bool ShouldProcess(
        Dictionary<string, Dictionary<string, CouponRecord>> history,
        string accountId,
        string code)
    {
        if (!history.TryGetValue(accountId, out var perAccount)) return true;
        if (!perAccount.TryGetValue(code, out var rec)) return true;
        return !IsCompletedStatus(rec.Status);
    }

    internal static bool IsCompletedStatus(string? status) =>
        status is "success" or "already" or "expired" or "invalid";

    internal static List<string> GetNewCodes(IEnumerable<string> currentCodes, ISet<string> seenCodes) =>
        currentCodes.Where(code => !seenCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code)
            .ToList();

    internal static string NormalizeServer(string? server)
    {
        var value = server?.Trim().ToLowerInvariant() ?? "";
        return ServerChoices.Any(choice => choice.Value == value) ? value : "korea";
    }

    private async Task RunAsync(bool _)
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
            if (!IsDone(a.Id, c))
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
                SetStatus($"쿠폰 받는 중 · {i + 1} / {queue.Count} · {item.Account.Name} · {item.Code} 확인 중...");

                string status;
                string message;
                try
                {
                    (status, message) = await RedeemAsync(item, _workCts.Token);
                }
                catch (OperationCanceledException) when (_workCts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    status = "error";
                    message = "이 쿠폰 처리 중 오류가 발생했습니다: " + ex.Message;
                }
                Record(item, status, message);

                _results.Items.Insert(0, $"{item.Account.Name} · {item.Code} · {DisplayStatus(status)}");
                _storage.Save(_state);
            }

            SetStatus($"완료 · 쿠폰 후보 {queue.Count}개 처리");
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
            // 같은 URL을 연속 지정하면 WebView2가 NavigationCompleted를 발생시키지 않을 수 있다.
            // 매 작업마다 고유 쿼리를 붙여 새로운 페이지 탐색을 보장한다.
            var navigationUrl = CouponUrl + "?_scm=" + Guid.NewGuid().ToString("N");
            _web.CoreWebView2.Navigate(navigationUrl);
            using var navigationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            navigationCts.CancelAfter(TimeSpan.FromSeconds(20));
            using var reg = navigationCts.Token.Register(() => tcs.TrySetCanceled(navigationCts.Token));
            bool ok;
            try
            {
                ok = await tcs.Task;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ("error", "공식 쿠폰 페이지를 제한 시간 안에 열지 못했습니다.");
            }
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
        var serverJson = JsonSerializer.Serialize(NormalizeServer(item.Account.Server));

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

          const requestedServer = {{serverJson}};
          const serverOption = [...sel.options].find(o =>
            String(o.value || '').toLowerCase() === requestedServer.toLowerCase());
          if (!serverOption) return JSON.stringify({ok:false,reason:'선택한 서버 옵션을 찾지 못했습니다: ' + requestedServer});
          sel.value = serverOption.value;
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

        var confirmationClicked = false;
        for (var i = 0; i < 120; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(250, ct);

            var poll = await _web.ExecuteScriptAsync("""
            (() => {
              const visible = el => {
                if (!el) return false;
                const style = getComputedStyle(el);
                return style.display !== 'none' && style.visibility !== 'hidden' &&
                       (el.offsetParent !== null || el.getClientRects().length > 0);
              };
              const result = document.querySelector('#EVTpop_1');
              if (visible(result)) {
                const message = result.querySelector('p')?.innerText ||
                                result.innerText || result.textContent || '';
                return JSON.stringify({kind:'result', message:message.trim()});
              }
              const confirm = document.querySelector('#EVTpop_coupon');
              if (visible(confirm)) {
                const message = confirm.querySelector('p')?.innerText ||
                                confirm.innerText || confirm.textContent || '';
                return JSON.stringify({kind:'confirm', message:message.trim()});
              }
              return JSON.stringify({kind:'waiting', message:''});
            })();
            """);

            var pollJson = UnwrapJsString(poll);
            using var pollDoc = JsonDocument.Parse(pollJson);
            var root = pollDoc.RootElement;
            var kind = root.GetProperty("kind").GetString() ?? "waiting";
            var message = root.GetProperty("message").GetString()?.Trim() ?? "";

            if (kind == "confirm" && !confirmationClicked)
            {
                var clicked = await _web.ExecuteScriptAsync("""
                (() => {
                  const button = document.querySelector('#EVTbtn_1');
                  if (!button) return false;
                  button.click();
                  return true;
                })();
                """);
                if (!string.Equals(clicked, "true", StringComparison.OrdinalIgnoreCase))
                    return ("error", "쿠폰 사용 확인 버튼을 누르지 못했습니다.");
                confirmationClicked = true;
                continue;
            }

            if (kind != "result" || message.Length < 2) continue;

            var status = Classify(message);
            await _web.ExecuteScriptAsync("document.querySelector('#EVTpop_1 .btn_confirm')?.click();");
            return (status, message);
        }

        return ("error", confirmationClicked
            ? "쿠폰 사용 요청 후 결과를 제한 시간 안에 받지 못했습니다."
            : "쿠폰 확인 결과를 제한 시간 안에 받지 못했습니다.");
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

    internal static string Classify(string message)
    {
        var m = message.ToLowerInvariant();

        if (Regex.IsMatch(m, "already|used|이미\\s*사용|사용한|등록된")) return "already";
        if (Regex.IsMatch(m, "expired|만료")) return "expired";
        if (Regex.IsMatch(m, "success|complete|reward|성공|완료|보상|지급")) return "success";
        if (Regex.IsMatch(m, "invalid|not valid|유효하지|유효한.*아닙니다|존재하지|wrong|잘못된|없는 쿠폰")) return "invalid";
        if (Regex.IsMatch(m, "error|오류|fail|실패")) return "error";
        // Unrecognized Hive responses are operational failures, not final coupon
        // verdicts. Keeping them retryable prevents a UI wording change from losing a
        // real coupon while still making error the only retryable stored status.
        return "error";
    }

    private static string DisplayStatus(string status) => status switch
    {
        "success" => "성공",
        "already" => "이미 사용",
        "expired" => "만료",
        "invalid" => "사용할 수 없음",
        "error" => "오류 - 다음 실행에서 재시도",
        _ => status
    };

    private sealed record ServerChoice(string Value, string DisplayName);

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
