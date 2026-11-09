// 세션 위젯 (윈도우 앱판)
// Claude 앱 플러그인 session-board 가 남기는 sessions.json 을 읽어 화면에 늘 떠 있는 창으로 보여 준다.
// 표시(강조 · 읽음 · 알람 · 메모 · 미루기 · 확인 끝)는 meta.json 을 플러그인과 함께 쓴다.
// 빌드: 같은 폴더 build.ps1 (윈도우 기본 C# 컴파일러 csc.exe, .NET Framework 4.x — C# 5 문법만: $"" · ?. 안 됨)
// 바꾸고 싶은 값(색 · 양식 · 주기 · 문구)은 바로 아래 Config 에 모여 있다. 창 바탕 · 버튼 모양은 Xaml 안.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

[assembly: AssemblyTitle("세션 위젯")]
[assembly: AssemblyProduct("세션 위젯")]
[assembly: AssemblyDescription("Claude 세션 현황 위젯")]
[assembly: AssemblyCompany("session-board")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace SessionWidget
{
    /// 바꾸고 싶은 값은 여기 (CUSTOMIZING.md). 고친 뒤 떠 있는 위젯을 끄고 build.ps1.
    static class Config
    {
        /// 창 제목 · 트레이 글
        public const string Title = "세션 위젯";
        /// 플러그인과 나눠 쓰는 폴더(홈 폴더 아래). 플러그인 hooks/config.ts 의 SHARE_DIR 과 같은 자리여야 한다
        public const string ShareDirUnderHome = @".claude\session-board";
        /// sessions.json · meta.json 을 다시 읽는 주기(초)
        public const double PollSeconds = 1;
        /// 이 시간(초) 넘게 sessions.json 이 안 바뀌면 「갱신 없음」
        public const int StaleSeconds = 120;
        /// 시간대(UTC 와의 차, 시간). null = 이 PC 의 시간대. 9 = 한국
        public static readonly double? TzOffsetHours = null;
        /// 카드의 시각 양식 (26.11.05 14:46)
        public const string ClockFormat = "yy.MM.dd HH:mm";
        /// 머리줄의 갱신 시각 양식
        public const string TimeFormat = "HH:mm:ss";
        /// 「내일 n시」 리마인드의 n
        public const int TomorrowHour = 9;

        // 카드 색
        public const string ColorRunning = "#F5B14C";
        public const string ColorNeeds = "#FF7AA2";
        public const string ColorNeedsAsk = "#FF9DBA";
        public const string ColorUnread = "#6CB6FF";
        public const string ColorRead = "#80FFFFFF";
        public const string ColorDue = "#FF6B6B";
        public const string ColorRemind = "#9AD0A0";
        public const string ColorStar = "#FFD866";
        public const string ColorRemoteOn = "#7EE2A8";
        public const string ColorWarn = "#FFB86B";
        public const string ColorError = "#FF7B7B";

        // 머리줄 문구
        public const string MsgWaiting = "현황 기다리는 중 — 플러그인을 설치한 뒤 Claude 앱에서 새 세션을 하나 열면 채워집니다";
        public const string MsgStale = "⚠ {0}부터 갱신 없음 — Claude 앱에 열린 세션이 없는 듯 (새 세션을 열면 다시 돕니다)";
    }

    class Session
    {
        public string Id = "";
        public string Title = "";
        public string LastActivityAt = "";
        public string Link = "";
        public string Ask = "";
        public bool IsRunning;
        public bool Remote;
        public bool Needs;
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            // 한 PC 에 하나만: 둘이 같이 떠서 알림이 겹치지 않게
            using (var mutex = new Mutex(true, "session-board-widget", out created))
            {
                if (!created)
                {
                    MessageBox.Show(Config.Title + "이 이미 떠 있습니다. 작업 표시줄 오른쪽 트레이 아이콘을 눌러 보세요.", Config.Title);
                    return;
                }
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var widget = new Widget();
                app.Run(widget.Window);
            }
        }
    }

    class Widget
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "SessionWidget";

        readonly string dir;
        readonly string sessionsFile;
        readonly string metaFile;
        readonly string settingsFile;
        readonly string exePath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly UTF8Encoding utf8 = new UTF8Encoding(false);

        public Window Window;
        FrameworkElement header;
        TextBlock summary;
        TextBlock subLine;
        TextBlock accountLine;
        string accountText = "";
        StackPanel list;
        Button btnPin;
        WinForms.NotifyIcon tray;
        WinForms.ToolStripMenuItem startupItem;
        DispatcherTimer timer;

        List<Session> sessions = new List<Session>();
        long updatedAtMs;
        string panelError = "";
        Dictionary<string, Dictionary<string, object>> meta = new Dictionary<string, Dictionary<string, object>>();
        Dictionary<string, bool> prevRunning = new Dictionary<string, bool>();
        HashSet<string> alerted = new HashSet<string>();
        bool firstLoad = true;
        DateTime sessionsTime = DateTime.MinValue;
        DateTime metaTime = DateTime.MinValue;
        long lastRender;
        bool showHeld;
        bool showDone;
        Button btnHeld;
        Button btnDone;
        // 여러 개 고르기: Ctrl 클릭 · Shift 클릭 · 「☑ 여러 개 고르기」 켜고 클릭
        Button btnSelect;
        FrameworkElement selBar;
        TextBlock selCount;
        HashSet<string> selected = new HashSet<string>();
        bool selectMode;
        string anchorId;
        List<string> renderOrder = new List<string>();

        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='{TITLE}' Width='340' Height='520' MinWidth='240' MinHeight='160'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent'
        Topmost='True' ResizeMode='CanResizeWithGrip' ShowInTaskbar='True'
        FontFamily='Malgun Gothic' FontSize='12'>
  <Window.Resources>
    <Style TargetType='Button'>
      <Setter Property='Foreground' Value='#DDFFFFFF'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='#22FFFFFF' CornerRadius='4' Padding='7,2' Margin='2,0,0,0'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='#55FFFFFF'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>
  <Border CornerRadius='10' Background='#EE1E1F24' BorderBrush='#40FFFFFF' BorderThickness='1'>
    <DockPanel>
      <Grid x:Name='Header' DockPanel.Dock='Top' Margin='10,8,8,6' Background='Transparent'>
        <StackPanel>
          <DockPanel Margin='0,0,0,4'>
            <StackPanel DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Top' Margin='6,-2,0,0'>
              <Button x:Name='BtnPin' ToolTip='항상 위에'/>
              <Button x:Name='BtnMin' Content='—' ToolTip='트레이로 숨기기'/>
              <Button x:Name='BtnClose' Content='✕' ToolTip='끄기'/>
            </StackPanel>
            <TextBlock DockPanel.Dock='Left' Text='{TITLE}' Foreground='#66FFFFFF' FontSize='10.5' VerticalAlignment='Center'/>
            <TextBlock x:Name='Account' Foreground='#88FFFFFF' FontSize='10.5' VerticalAlignment='Center'
                       HorizontalAlignment='Right' TextTrimming='CharacterEllipsis' Margin='8,0,0,0'
                       ToolTip='Claude 앱에 로그인한 계정 · 조직'/>
          </DockPanel>
          <TextBlock x:Name='Summary' Foreground='White' FontWeight='Bold' FontSize='13' TextWrapping='Wrap'/>
          <TextBlock x:Name='SubLine' Foreground='#99FFFFFF' FontSize='11' TextWrapping='Wrap'/>
          <StackPanel Orientation='Horizontal' Margin='-2,5,0,0'>
            <Button x:Name='BtnSelect' ToolTip='켜면 카드를 누를 때마다 골라짐 · 꺼져 있어도 Ctrl 클릭 · Shift 클릭으로 고를 수 있음 · Esc 로 풀기'/>
            <Button x:Name='BtnHeld' ToolTip='⏸ 미룬 것(일단 보류): 「미루기 풀기」 할 때까지 접혀 있음 · 펼치기 / 접기'/>
            <Button x:Name='BtnDone' ToolTip='✓ 확인 끝: 새 대화가 시작되거나 새 답이 오면 다시 올라옴 · 펼치기 / 접기'/>
          </StackPanel>
        </StackPanel>
      </Grid>
      <TextBlock DockPanel.Dock='Bottom' Margin='10,2,10,8' Foreground='#66FFFFFF' FontSize='10.5' TextWrapping='Wrap'
                 Text='클릭: 앱에서 열기 + 읽음 · Ctrl / Shift 클릭: 여러 개 고르기 · 오른쪽 클릭: 강조 · 미루기 · 확인 끝 · 알람 · 메모'/>
      <Border x:Name='SelBar' DockPanel.Dock='Bottom' Margin='6,4,6,2' Padding='8,6,6,6' CornerRadius='6'
              Background='#33508CFF' BorderBrush='#808FB8FF' BorderThickness='1' Visibility='Collapsed'>
        <StackPanel>
          <TextBlock x:Name='SelCount' Foreground='White' FontWeight='Bold' TextWrapping='Wrap' Margin='0,0,0,5'/>
          <WrapPanel>
            <Button x:Name='SelDone' Content='✓ 확인 끝' Margin='0,0,4,4'/>
            <Button x:Name='SelHold' Content='⏸ 미루기' Margin='0,0,4,4'/>
            <Button x:Name='SelUp' Content='⬆ 목록으로' ToolTip='미루기 · 확인 끝을 풀고 목록으로 되돌림' Margin='0,0,4,4'/>
            <Button x:Name='SelRead' Content='읽음으로' Margin='0,0,4,4'/>
            <Button x:Name='SelStar' Content='★ 강조' Margin='0,0,4,4'/>
            <Button x:Name='SelAll' Content='목록 전부 고르기' ToolTip='Ctrl+A' Margin='0,0,4,4'/>
            <Button x:Name='SelClear' Content='고르기 끝' ToolTip='Esc' Margin='0,0,4,4'/>
          </WrapPanel>
        </StackPanel>
      </Border>
      <ScrollViewer VerticalScrollBarVisibility='Auto' Margin='6,0,4,0'>
        <StackPanel x:Name='List'/>
      </ScrollViewer>
    </DockPanel>
  </Border>
</Window>";

        public Widget()
        {
            json.MaxJsonLength = int.MaxValue;
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Config.ShareDirUnderHome);
            sessionsFile = Path.Combine(dir, "sessions.json");
            metaFile = Path.Combine(dir, "meta.json");
            exePath = Assembly.GetExecutingAssembly().Location;
            settingsFile = Path.Combine(Path.GetDirectoryName(exePath), "widget-settings.json");

            Window = (Window)XamlReader.Parse(Xaml.Replace("{TITLE}", Config.Title));
            header = (FrameworkElement)Window.FindName("Header");
            summary = (TextBlock)Window.FindName("Summary");
            subLine = (TextBlock)Window.FindName("SubLine");
            accountLine = (TextBlock)Window.FindName("Account");
            list = (StackPanel)Window.FindName("List");
            btnHeld = (Button)Window.FindName("BtnHeld");
            btnDone = (Button)Window.FindName("BtnDone");
            btnPin = (Button)Window.FindName("BtnPin");
            var btnMin = (Button)Window.FindName("BtnMin");
            var btnClose = (Button)Window.FindName("BtnClose");

            LoadSettings();

            header.MouseLeftButtonDown += (s, e) => { try { Window.DragMove(); } catch { } };
            btnHeld.Click += (s, e) => { showHeld = !showHeld; SaveSettings(); Render(); };
            btnDone.Click += (s, e) => { showDone = !showDone; SaveSettings(); Render(); };
            btnPin.Click += (s, e) => { Window.Topmost = !Window.Topmost; SaveSettings(); Render(); };

            btnSelect = (Button)Window.FindName("BtnSelect");
            selBar = (FrameworkElement)Window.FindName("SelBar");
            selCount = (TextBlock)Window.FindName("SelCount");
            btnSelect.Click += (s, e) => { if (selectMode) EndSelect(); else { selectMode = true; Render(); } };
            ((Button)Window.FindName("SelDone")).Click += (s, e) => Bulk("done");
            ((Button)Window.FindName("SelHold")).Click += (s, e) => Bulk("hold");
            ((Button)Window.FindName("SelUp")).Click += (s, e) => Bulk("up");
            ((Button)Window.FindName("SelRead")).Click += (s, e) => Bulk("read");
            ((Button)Window.FindName("SelStar")).Click += (s, e) => Bulk("star");
            ((Button)Window.FindName("SelAll")).Click += (s, e) => SelectAllMain();
            ((Button)Window.FindName("SelClear")).Click += (s, e) => EndSelect();
            Window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && (selectMode || selected.Count > 0)) { EndSelect(); e.Handled = true; }
                else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { SelectAllMain(); e.Handled = true; }
            };
            btnMin.Click += (s, e) => Window.Hide();
            btnClose.Click += (s, e) => Window.Close();

            SetupTray();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(Config.PollSeconds);
            timer.Tick += (s, e) => SafePoll();

            Window.Closing += (s, e) =>
            {
                timer.Stop();
                try { SaveSettings(); } catch { }
                tray.Visible = false;
                tray.Dispose();
            };

            SafePoll();
            Render();
            timer.Start();
        }

        // ---------- 트레이 · 시작 프로그램 ----------

        void SetupTray()
        {
            tray = new WinForms.NotifyIcon();
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath); }
            catch { tray.Icon = System.Drawing.SystemIcons.Information; }
            tray.Text = Config.Title;
            tray.Visible = true;
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != WinForms.MouseButtons.Left) return;
                if (Window.IsVisible) Window.Hide(); else ShowWindow();
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("보이기", null, (s, e) => ShowWindow());
            startupItem = new WinForms.ToolStripMenuItem("윈도우 시작할 때 같이 켜기");
            startupItem.Checked = IsStartup();
            startupItem.Click += (s, e) =>
            {
                SetStartup(!IsStartup());
                startupItem.Checked = IsStartup();
            };
            menu.Items.Add(startupItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("끄기", null, (s, e) => Window.Close());
            tray.ContextMenuStrip = menu;
        }

        void ShowWindow()
        {
            Window.Show();
            Window.Activate();
        }

        bool IsStartup()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return k != null && k.GetValue(RunName) != null;
            }
        }

        void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + exePath + "\"");
                else k.DeleteValue(RunName, false);
            }
        }

        void Alert(string title, string text, bool loud)
        {
            if (loud) System.Media.SystemSounds.Exclamation.Play();
            else System.Media.SystemSounds.Asterisk.Play();
            tray.ShowBalloonTip(10000, title, text, WinForms.ToolTipIcon.Info);
        }

        // ---------- 설정 (자리 · 크기 · 보기) ----------

        void LoadSettings()
        {
            var d = ReadJson(settingsFile) as Dictionary<string, object>;
            if (d == null)
            {
                var wa = SystemParameters.WorkArea;
                Window.Left = wa.Right - Window.Width - 16;
                Window.Top = wa.Top + 60;
                return;
            }
            if (d.ContainsKey("Width")) Window.Width = ToDouble(d["Width"]);
            if (d.ContainsKey("Height")) Window.Height = ToDouble(d["Height"]);
            if (d.ContainsKey("Left")) Window.Left = ToDouble(d["Left"]);
            if (d.ContainsKey("Top")) Window.Top = ToDouble(d["Top"]);
            if (d.ContainsKey("Topmost")) Window.Topmost = d["Topmost"] is bool && (bool)d["Topmost"];
            showHeld = d.ContainsKey("showHeld") && d["showHeld"] is bool && (bool)d["showHeld"];
            showDone = d.ContainsKey("showDone") && d["showDone"] is bool && (bool)d["showDone"];

            // 모니터 구성이 바뀌어 화면 밖이면 오른쪽 위로
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!vs.Contains(new Point(Window.Left + 40, Window.Top + 20)))
            {
                var wa = SystemParameters.WorkArea;
                Window.Left = wa.Right - Window.Width - 16;
                Window.Top = wa.Top + 60;
            }
        }

        void SaveSettings()
        {
            var d = new Dictionary<string, object>();
            d["Left"] = Window.Left;
            d["Top"] = Window.Top;
            d["Width"] = Window.Width;
            d["Height"] = Window.Height;
            d["Topmost"] = Window.Topmost;
            d["showHeld"] = showHeld;
            d["showDone"] = showDone;
            WriteJson(settingsFile, d);
        }

        // ---------- 파일 ----------

        object ReadJson(string path)
        {
            if (!File.Exists(path)) return null;
            for (int i = 0; i < 3; i++)
            {
                try { return json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)); }
                catch { Thread.Sleep(80); }   // 플러그인이 쓰는 중이면 잠깐 뒤 다시
            }
            return null;
        }

        void WriteJson(string path, object obj)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json.Serialize(obj), utf8);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch { File.Copy(tmp, path, true); File.Delete(tmp); return; }
            }
            File.Move(tmp, path);
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static bool Bool(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v is bool && (bool)v;
        }

        static long Long(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static double ToDouble(object v)
        {
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        Dictionary<string, Dictionary<string, object>> LoadMeta()
        {
            var map = new Dictionary<string, Dictionary<string, object>>();
            var raw = ReadJson(metaFile) as Dictionary<string, object>;
            if (raw == null) return map;
            foreach (var kv in raw)
            {
                var m = kv.Value as Dictionary<string, object>;
                map[kv.Key] = m != null ? new Dictionary<string, object>(m) : new Dictionary<string, object>();
            }
            return map;
        }

        /// 플러그인이 그사이 바꾼 것을 먼저 읽고 그 위에 바꾼다
        void ChangeMeta(string id, Action<Dictionary<string, object>> fn)
        {
            var map = LoadMeta();
            if (!map.ContainsKey(id)) map[id] = new Dictionary<string, object>();
            fn(map[id]);
            foreach (var k in map[id].Keys.ToList())
            {
                var v = map[id][k];
                if (v == null || (v is string && (string)v == "")) map[id].Remove(k);
            }
            try { WriteJson(metaFile, map); }
            catch (Exception ex) { subLine.Text = "표시 저장 실패: " + ex.Message; }
            meta = map;
            Render();
        }

        Dictionary<string, object> MetaOf(string id)
        {
            Dictionary<string, object> m;
            return meta.TryGetValue(id, out m) ? m : null;
        }

        // ---------- 시간 (이 PC 의 시간대 또는 Config.TzOffsetHours, 양식 26.11.05 14:46) ----------

        static long NowMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

        static DateTimeOffset ToLocal(long ms)
        {
            var d = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            return Config.TzOffsetHours.HasValue ? d.ToOffset(TimeSpan.FromHours(Config.TzOffsetHours.Value)) : d.ToLocalTime();
        }

        static string ClockText(long ms) { return ToLocal(ms).ToString(Config.ClockFormat, CultureInfo.InvariantCulture); }

        static string TimeText(long ms) { return ToLocal(ms).ToString(Config.TimeFormat, CultureInfo.InvariantCulture); }

        static long IsoMs(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return 0;
            DateTimeOffset d;
            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d) ? d.ToUnixTimeMilliseconds() : 0;
        }

        static string AgoText(long ms)
        {
            long min = (NowMs() - ms) / 60000;
            if (min < 1) return "방금";
            if (min < 60) return min + "분 전";
            long hr = min / 60;
            if (hr < 24) return hr + "시간 전";
            return (hr / 24) + "일 전";
        }

        static long TomorrowNine()
        {
            var t = ToLocal(NowMs());
            var d = new DateTimeOffset(t.Year, t.Month, t.Day, Config.TomorrowHour, 0, 0, t.Offset);
            return d.AddDays(1).ToUnixTimeMilliseconds();
        }

        // ---------- 상태 판단 ----------

        static bool IsUnread(Session s, Dictionary<string, object> m)
        {
            if (s.IsRunning) return false;
            string seen = Str(m, "seenAt");
            if (seen == "") return false;
            return string.CompareOrdinal(s.LastActivityAt, seen) > 0;
        }

        static bool IsDue(Dictionary<string, object> m)
        {
            long at = Long(m, "remindAt");
            return at > 0 && at <= NowMs();
        }

        /// 확인 필요: 읽어도 그대로, 사용자가 현황판에서 내릴 때만 사라진다
        static bool IsNeeds(Session s) { return !s.IsRunning && s.Needs; }

        /// ⏸ 미루기(일단 보류): 사용자가 풀 때까지. 단, 리마인드가 울리면 목록에 나온다
        static bool IsHeld(Session s, Dictionary<string, object> m)
        {
            return Str(m, "heldAt") != "" && !IsDue(m);
        }

        /// 미룬 뒤로 새 답이 왔는가(접힌 칸에 표시만)
        static bool HeldNew(Session s, Dictionary<string, object> m)
        {
            string at = Str(m, "heldAt");
            return at != "" && !s.IsRunning && string.CompareOrdinal(s.LastActivityAt, at) > 0;
        }

        /// ✓ 확인 끝: 그 뒤로 새 활동(새 대화 · 새 답)이 없는 동안만
        static bool IsDone(Session s, Dictionary<string, object> m)
        {
            if (Str(m, "heldAt") != "") return false;
            string at = Str(m, "hiddenAt");
            return at != "" && string.CompareOrdinal(s.LastActivityAt, at) <= 0;
        }

        /// 목록 본문에서 빠지는가
        static bool IsHidden(Session s, Dictionary<string, object> m)
        {
            return IsHeld(s, m) || IsDone(s, m);
        }

        static int Rank(Session s, Dictionary<string, object> m)
        {
            if (IsDue(m)) return 0;
            if (IsNeeds(s)) return 1;
            if (Bool(m, "star")) return 2;
            if (IsUnread(s, m)) return 3;
            if (s.IsRunning) return 4;
            return 5;
        }

        // ---------- 주기적으로 읽기 ----------

        void SafePoll()
        {
            try { Poll(); }
            catch (Exception ex) { subLine.Text = "위젯 오류: " + ex.Message; }
        }

        void Poll()
        {
            bool changed = false;

            if (File.Exists(sessionsFile))
            {
                var t = File.GetLastWriteTimeUtc(sessionsFile);
                if (t != sessionsTime)
                {
                    var d = ReadJson(sessionsFile) as Dictionary<string, object>;
                    if (d != null)
                    {
                        sessionsTime = t;
                        updatedAtMs = Long(d, "updatedAtMs");
                        panelError = Str(d, "error");
                        var acc = Get(d, "account") as Dictionary<string, object>;
                        string who = Str(acc, "name");
                        if (who == "") who = Str(acc, "email");
                        string org = Str(acc, "org");
                        accountText = who + (org != "" ? (who != "" ? " · " : "") + org : "");
                        var rows = new List<Session>();
                        var arr = Get(d, "sessions") as IEnumerable;
                        if (arr != null)
                        {
                            foreach (var o in arr)
                            {
                                var r = o as Dictionary<string, object>;
                                if (r == null) continue;
                                rows.Add(new Session
                                {
                                    Id = Str(r, "id"),
                                    Title = Str(r, "title"),
                                    LastActivityAt = Str(r, "lastActivityAt"),
                                    Link = Str(r, "link"),
                                    Ask = Str(r, "ask"),
                                    IsRunning = Bool(r, "isRunning"),
                                    Remote = Bool(r, "remote"),
                                    Needs = Bool(r, "needs"),
                                });
                            }
                        }
                        // 답 왔음 · 확인 필요: 작업 중이던 세션이 멈춤
                        if (!firstLoad)
                        {
                            foreach (var s in rows)
                            {
                                bool was;
                                // 미룬 세션은 알리지 않는다(접힌 칸에 「새 답 있음」만)
                                if (prevRunning.TryGetValue(s.Id, out was) && was && !s.IsRunning &&
                                    Str(MetaOf(s.Id), "heldAt") == "")
                                {
                                    if (IsNeeds(s)) Alert("🙋 확인 필요", s.Title + (s.Ask != "" ? "\n→ " + s.Ask : ""), true);
                                    else Alert("💬 답 왔음", s.Title, false);
                                }
                            }
                        }
                        prevRunning = new Dictionary<string, bool>();
                        foreach (var s in rows) prevRunning[s.Id] = s.IsRunning;
                        sessions = rows;
                        firstLoad = false;
                        changed = true;
                    }
                }
            }

            if (File.Exists(metaFile))
            {
                var t = File.GetLastWriteTimeUtc(metaFile);
                if (t != metaTime) { metaTime = t; meta = LoadMeta(); changed = true; }
            }

            // 리마인드: 이 위젯에서 아직 안 울린 것
            foreach (var s in sessions)
            {
                var m = MetaOf(s.Id);
                if (!IsDue(m)) continue;
                string key = s.Id + "|" + Long(m, "remindAt");
                if (alerted.Contains(key)) continue;
                alerted.Add(key);
                string memo = Str(m, "memo");
                Alert("⏰ 리마인드", s.Title + (memo != "" ? " · " + memo : ""), true);
                changed = true;
            }

            if (changed || NowMs() - lastRender > 30000) Render();
        }

        // ---------- 그리기 ----------

        static Brush B(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }

        void Render()
        {
            long now = NowMs();
            lastRender = now;
            int heldCount = sessions.Count(s => IsHeld(s, MetaOf(s.Id)));
            int heldNewCount = sessions.Count(s => IsHeld(s, MetaOf(s.Id)) && HeldNew(s, MetaOf(s.Id)));
            int doneCount = sessions.Count(s => IsDone(s, MetaOf(s.Id)));
            btnHeld.Content = (showHeld ? "▾ " : "▸ ") + "⏸ 미룬 것 " + heldCount + (heldNewCount > 0 ? " · 새 답 " + heldNewCount : "");
            btnHeld.Visibility = heldCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnDone.Content = (showDone ? "▾ " : "▸ ") + "✓ 확인 끝 " + doneCount;
            btnDone.Visibility = doneCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnPin.Content = Window.Topmost ? "📌" : "📍";

            var visible = sessions.Where(s => !IsHidden(s, MetaOf(s.Id))).ToList();
            int running = visible.Count(s => s.IsRunning);
            int unread = visible.Count(s => IsUnread(s, MetaOf(s.Id)));
            int due = visible.Count(s => IsDue(MetaOf(s.Id)));
            int needs = visible.Count(s => IsNeeds(s));
            summary.Text = "확인 필요 " + needs + " · 작업 중 " + running + " · 안 읽음 " + unread + (due > 0 ? " · ⏰ " + due : "");

            accountLine.Text = accountText;
            if (updatedAtMs == 0)
            {
                subLine.Text = Config.MsgWaiting;
                subLine.Foreground = B(Config.ColorWarn);
            }
            else if (panelError != "")
            {
                subLine.Text = panelError;
                subLine.Foreground = B(Config.ColorError);
            }
            else if (now - updatedAtMs > Config.StaleSeconds * 1000L)
            {
                subLine.Text = string.Format(Config.MsgStale, AgoText(updatedAtMs));
                subLine.Foreground = B(Config.ColorWarn);
            }
            else
            {
                subLine.Text = "세션 " + visible.Count + "개 · 갱신 " + TimeText(updatedAtMs);
                subLine.Foreground = B("#99FFFFFF");
            }

            // 목록에서 사라진 세션은 고른 것에서도 뺀다
            var alive = new HashSet<string>(sessions.Select(x => x.Id));
            selected.RemoveWhere(x => !alive.Contains(x));
            UpdateSelBar();
            renderOrder = new List<string>();

            list.Children.Clear();
            var sorted = sessions
                .OrderBy(s => Rank(s, MetaOf(s.Id)))
                .ThenByDescending(s => s.LastActivityAt, StringComparer.Ordinal)
                .ToList();
            foreach (var s in sorted)
            {
                var m = MetaOf(s.Id);
                if (!IsHidden(s, m)) list.Children.Add(MakeCard(s, m, "main"));
            }

            if (showHeld && heldCount > 0)
            {
                list.Children.Add(SectionLine("⏸ 미룬 것 — 「미루기 풀기」 할 때까지 여기 · 알림 없음 · 리마인드는 울림"));
                foreach (var s in sorted.Where(x => IsHeld(x, MetaOf(x.Id))))
                    list.Children.Add(MakeCard(s, MetaOf(s.Id), "held"));
            }
            if (showDone && doneCount > 0)
            {
                list.Children.Add(SectionLine("✓ 확인 끝 — 새 대화가 시작되거나 새 답이 오면 다시 올라옴"));
                foreach (var s in sorted.Where(x => IsDone(x, MetaOf(x.Id))))
                    list.Children.Add(MakeCard(s, MetaOf(s.Id), "done"));
            }
        }

        static TextBlock SectionLine(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.TextWrapping = TextWrapping.Wrap;
            t.Foreground = B("#88FFFFFF");
            t.FontSize = 11;
            t.FontWeight = FontWeights.Bold;
            t.Margin = new Thickness(4, 8, 0, 4);
            return t;
        }

        /// kind: main(목록) · held(미룬 것) · done(확인 끝)
        Border MakeCard(Session s, Dictionary<string, object> m, string kind)
        {
            bool isLowered = kind != "main";
            bool isPicked = selected.Contains(s.Id);
            renderOrder.Add(s.Id);
            bool unreadNow = IsUnread(s, m);
            bool needsNow = IsNeeds(s);
            bool star = Bool(m, "star");
            long last = IsoMs(s.LastActivityAt);

            string state; string color;
            if (s.IsRunning) { state = "● 작업 중"; color = Config.ColorRunning; }
            else if (needsNow) { state = "🙋 확인 필요"; color = Config.ColorNeeds; }
            else if (unreadNow) { state = "💬 답 왔음 · 안 읽음"; color = Config.ColorUnread; }
            else { state = "✓ 읽음"; color = Config.ColorRead; }
            string accent = IsDue(m) ? Config.ColorDue : color;

            var card = new Border();
            card.CornerRadius = new CornerRadius(6);
            card.Background = B(needsNow ? "#2EFF7AA2" : unreadNow ? "#2A6CB6FF" : "#14FFFFFF");
            card.BorderBrush = B(accent);
            card.BorderThickness = new Thickness(3, 0, 0, 0);
            card.Margin = new Thickness(0, 0, 0, 5);
            card.Padding = new Thickness(8, 5, 8, 5);
            card.Cursor = Cursors.Hand;
            card.ToolTip = s.Title;
            if (isLowered) card.Opacity = kind == "held" ? 0.7 : 0.45;
            if (isPicked)
            {
                // 고른 카드: 파란 바탕 + 테두리, 흐린 칸에 있어도 또렷하게
                card.Background = B("#55508CFF");
                card.BorderBrush = B("#8FB8FF");
                card.BorderThickness = new Thickness(3, 1, 1, 1);
                card.Opacity = 1;
            }

            var stack = new StackPanel();

            var title = new TextBlock();
            string box = isPicked ? "☑ " : (selectMode || selected.Count > 0) ? "☐ " : "";
            title.Text = box + (kind == "held" ? "(미룸) " : kind == "done" ? "(확인 끝) " : "") + (star ? "★ " : "") + s.Title;
            title.Foreground = B(star ? Config.ColorStar : "#FFFFFF");
            title.FontWeight = (needsNow || unreadNow || star) ? FontWeights.Bold : FontWeights.Normal;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(title);

            // 상태 · 시간 · 원격 제어
            var line = new TextBlock();
            line.FontSize = 11;
            line.TextWrapping = TextWrapping.Wrap;
            var r1 = new Run(state);
            r1.Foreground = B(color);
            if (needsNow) r1.FontWeight = FontWeights.Bold;
            var r2 = new Run(" · " + (last > 0 ? AgoText(last) + " (" + ClockText(last) + ")" : "시간 모름"));
            r2.Foreground = B("#99FFFFFF");
            var r3 = new Run(s.Remote ? " · 📡 원격 켜짐" : " · 원격 꺼짐");
            r3.Foreground = B(s.Remote ? Config.ColorRemoteOn : "#66FFFFFF");
            line.Inlines.Add(r1);
            line.Inlines.Add(r2);
            line.Inlines.Add(r3);
            stack.Children.Add(line);

            if (kind == "held" && HeldNew(s, m)) stack.Children.Add(SmallLine("💬 미룬 뒤 새 답 있음", Config.ColorUnread, true));
            if (needsNow && s.Ask != "") stack.Children.Add(SmallLine("→ " + s.Ask, Config.ColorNeedsAsk, false));

            long remindAt = Long(m, "remindAt");
            if (remindAt > 0)
            {
                if (IsDue(m)) stack.Children.Add(SmallLine("⏰ 리마인드 지남 (" + ClockText(remindAt) + ")", Config.ColorDue, true));
                else stack.Children.Add(SmallLine("⏰ " + ClockText(remindAt), Config.ColorRemind, false));
            }
            string memo = Str(m, "memo");
            if (memo != "") stack.Children.Add(SmallLine("📝 " + memo, "#B0FFFFFF", false));

            card.Child = stack;

            string id = s.Id;
            var menu = new ContextMenu();
            if (isPicked && selected.Count > 1)
            {
                // 고른 카드 위 오른쪽 클릭 = 고른 것 전부에
                var head = new MenuItem();
                head.Header = "고른 " + selected.Count + "개에 한꺼번에";
                head.IsEnabled = false;
                menu.Items.Add(head);
                AddItem(menu, "✓ 확인 끝", () => Bulk("done"));
                AddItem(menu, "⏸ 미루기 (일단 보류)", () => Bulk("hold"));
                AddItem(menu, "⬆ 목록으로 (미루기 · 확인 끝 풀기)", () => Bulk("up"));
                AddItem(menu, "읽음으로", () => Bulk("read"));
                AddItem(menu, "★ 강조 / 해제", () => Bulk("star"));
                menu.Items.Add(new Separator());
                AddItem(menu, "고르기 끝", () => EndSelect());
                card.ContextMenu = menu;
                card.MouseLeftButtonUp += (o, e) => OnCardClick(id);
                return card;
            }
            AddItem(menu, "앱에서 열기", () => DoAction("open", id));
            AddItem(menu, star ? "강조 해제" : "★ 강조", () => DoAction("star", id));
            AddItem(menu, unreadNow ? "읽음으로" : "안 읽음으로", () => DoAction("read", id));
            menu.Items.Add(new Separator());
            bool held = Str(m, "heldAt") != "";
            AddItem(menu, held ? "⬆ 미루기 풀기" : "⏸ 미루기 (일단 보류)", () => DoAction(held ? "unhold" : "hold", id));
            AddItem(menu, kind == "done" ? "⬆ 다시 올리기" : "✓ 확인 끝", () => DoAction(kind == "done" ? "undone" : "done", id));
            menu.Items.Add(new Separator());
            AddItem(menu, "⏰ 30분 뒤", () => DoAction("remind30", id));
            AddItem(menu, "⏰ 1시간 뒤", () => DoAction("remind60", id));
            AddItem(menu, "⏰ 3시간 뒤", () => DoAction("remind180", id));
            AddItem(menu, "⏰ 내일 " + Config.TomorrowHour + "시", () => DoAction("remindTomorrow", id));
            if (remindAt > 0) AddItem(menu, "알람 끄기", () => DoAction("remindOff", id));
            menu.Items.Add(new Separator());
            AddItem(menu, "📝 메모…", () => DoAction("memo", id));
            card.ContextMenu = menu;

            card.MouseLeftButtonUp += (o, e) => OnCardClick(id);
            return card;
        }

        // ---------- 여러 개 고르기 ----------

        void OnCardClick(string id)
        {
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Shift) != 0 && anchorId != null && renderOrder.Contains(anchorId))
            {
                // Shift 클릭: 지난번 고른 카드부터 여기까지 (보이는 순서대로)
                int a = renderOrder.IndexOf(anchorId), b = renderOrder.IndexOf(id);
                if (b < 0) return;
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) selected.Add(renderOrder[i]);
                Render();
                return;
            }
            if ((mods & (ModifierKeys.Control | ModifierKeys.Shift)) != 0 || selectMode)
            {
                if (!selected.Remove(id)) selected.Add(id);
                anchorId = id;
                Render();
                return;
            }
            // 보통 클릭: 앱에서 열기(고르던 것이 있으면 풀고)
            if (selected.Count > 0) { selected.Clear(); anchorId = null; }
            DoAction("open", id);
        }

        void SelectAllMain()
        {
            foreach (var s in sessions)
                if (!IsHidden(s, MetaOf(s.Id))) selected.Add(s.Id);
            selectMode = true;
            Render();
        }

        void EndSelect()
        {
            selected.Clear();
            anchorId = null;
            selectMode = false;
            Render();
        }

        void UpdateSelBar()
        {
            bool on = selectMode || selected.Count > 0;
            btnSelect.Content = on ? "☑ 고르는 중" : "☐ 여러 개 고르기";
            selBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            selCount.Text = selected.Count > 0
                ? "고른 세션 " + selected.Count + "개 — 아래에서 한꺼번에 처리"
                : "카드를 눌러 고르세요 (Shift 클릭 = 사이 전부)";
        }

        /// 고른 세션 전부에 한 번에 적용하고 파일은 한 번만 쓴다
        void Bulk(string kind)
        {
            if (selected.Count == 0) return;
            var picked = sessions.Where(x => selected.Contains(x.Id)).ToList();
            bool starAll = picked.Any(x => !Bool(MetaOf(x.Id), "star"));
            var map = LoadMeta();
            foreach (var s in picked)
            {
                if (!map.ContainsKey(s.Id)) map[s.Id] = new Dictionary<string, object>();
                var m = map[s.Id];
                switch (kind)
                {
                    case "done": m["hiddenAt"] = s.LastActivityAt; m.Remove("heldAt"); break;
                    case "hold": m["heldAt"] = s.LastActivityAt; m.Remove("hiddenAt"); break;
                    case "up": m.Remove("heldAt"); m.Remove("hiddenAt"); break;
                    case "read": if (!s.IsRunning) m["seenAt"] = s.LastActivityAt; break;
                    case "star": if (starAll) m["star"] = true; else m.Remove("star"); break;
                }
            }
            try { WriteJson(metaFile, map); }
            catch (Exception ex) { subLine.Text = "표시 저장 실패: " + ex.Message; return; }
            meta = map;
            // 처리한 뒤엔 고른 것만 비우고, 고르는 중이면 그대로 이어서 고를 수 있게
            selected.Clear();
            anchorId = null;
            Render();
        }

        static TextBlock SmallLine(string text, string color, bool bold)
        {
            var t = new TextBlock();
            t.Text = text;
            t.Foreground = B(color);
            t.FontSize = 11;
            t.TextWrapping = TextWrapping.Wrap;
            if (bold) t.FontWeight = FontWeights.Bold;
            return t;
        }

        static void AddItem(ContextMenu menu, string header, Action act)
        {
            var mi = new MenuItem();
            mi.Header = header;
            mi.Click += (o, e) => act();
            menu.Items.Add(mi);
        }

        // ---------- 동작 ----------

        void DoAction(string kind, string id)
        {
            var s = sessions.FirstOrDefault(x => x.Id == id);
            if (s == null) return;
            long now = NowMs();
            string iso = s.LastActivityAt;
            switch (kind)
            {
                case "open":
                    // 앱 전환을 먼저, 카드는 읽음 표시만 바로 바꾼다(확인 필요 · 카드 자체는 그대로)
                    if (s.Link != "")
                    {
                        try { Process.Start(s.Link); } catch { }
                    }
                    if (!s.IsRunning) ChangeMeta(id, m => { m["seenAt"] = iso; });
                    break;
                case "star":
                    ChangeMeta(id, m => { m["star"] = !Bool(m, "star"); });
                    break;
                case "read":
                    bool toRead = IsUnread(s, MetaOf(id));
                    ChangeMeta(id, m => { m["seenAt"] = toRead ? iso : "2000-01-01T00:00:00.000Z"; });
                    break;
                // 미루기와 확인 끝은 둘 중 하나만
                case "hold":
                    ChangeMeta(id, m => { m["heldAt"] = iso; m["hiddenAt"] = null; });
                    break;
                case "unhold":
                    ChangeMeta(id, m => { m["heldAt"] = null; });
                    break;
                case "done":
                    ChangeMeta(id, m => { m["hiddenAt"] = iso; m["heldAt"] = null; });
                    break;
                case "undone":
                    ChangeMeta(id, m => { m["hiddenAt"] = null; });
                    break;
                case "remind30": SetRemind(id, now + 30 * 60000L); break;
                case "remind60": SetRemind(id, now + 60 * 60000L); break;
                case "remind180": SetRemind(id, now + 180 * 60000L); break;
                case "remindTomorrow": SetRemind(id, TomorrowNine()); break;
                case "remindOff":
                    ChangeMeta(id, m => { m["remindAt"] = null; m["remindFired"] = null; });
                    break;
                case "memo":
                    string cur = Str(MetaOf(id), "memo");
                    bool wasTop = Window.Topmost;
                    Window.Topmost = false;
                    string text = Microsoft.VisualBasic.Interaction.InputBox(
                        "메모 (알람이 울릴 때 같이 뜸)\n\n" + s.Title, Config.Title + " 메모", cur, -1, -1);
                    Window.Topmost = wasTop;
                    string memo = (text ?? "").Trim();
                    if (memo == cur) break;
                    // 입력 상자는 「취소」도 빈 글로 돌려줘서, 있던 메모를 지울 때는 한 번 묻는다
                    if (memo == "" && cur != "" &&
                        MessageBox.Show("메모를 지울까요?\n\n" + cur, Config.Title + " 메모", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        break;
                    ChangeMeta(id, m => { m["memo"] = memo; });
                    break;
            }
        }

        void SetRemind(string id, long at)
        {
            ChangeMeta(id, m => { m["remindAt"] = at; m["remindFired"] = false; });
        }
    }
}
