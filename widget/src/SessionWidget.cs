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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

[assembly: AssemblyTitle("세션 위젯")]
[assembly: AssemblyProduct("세션 위젯")]
[assembly: AssemblyDescription("Claude 세션 현황 위젯")]
[assembly: AssemblyCompany("session-board")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

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
        /// 앱 창에서 지금 보고 있는 세션: 줄 왼쪽 가는 선 · 카드 테두리
        public const string ColorViewing = "#7FB8FF";

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
        /// 앱 사이드바 섹션(그룹) 이름, 없으면 ""
        public string Group = "";
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
        // 머리의 숫자 타일 셋 · 맨 아래 갱신 시각
        Border tileNeeds;
        TextBlock statNeeds, statRun, statUnread, needsLabel, updatedText;
        // 섹션마다 「읽은 것 n개 더 보기」 를 펼친 섹션 이름
        HashSet<string> openRead = new HashSet<string>();
        TextBlock subLine;
        TextBlock accountLine;
        string accountText = "";
        StackPanel list;
        Button btnPin;
        // 고정: auto = 항상 위(고정 버튼으로 잠깐 내리면 풀렸다가, 위젯을 다시 올리면 저절로 고정) · off = 고정 안 함
        string pinMode = "top";
        // 고정 단계(누를 때마다 돈다): top 📌 고정 → mid ↓📌 중간 → off 고정 해제 → top
        //  top: 늘 맨 위, 무엇을 눌러도 안 내려감(최소화 버튼도 막음)
        //  mid: 늘 맨 위, 밖을 눌러도 안 내려가지만 최소화 버튼으로는 내려감 → 작업 표시줄 · 트레이 아이콘으로 다시 띄움
        //  off: 보통 창 — 밖을 누르면 그 뒤로 감
        Button btnMinRef;
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
        // 위젯에 올린 세션만 본문에: meta board=true. 처음 보는 세션(knownIds 에 없음)은 저절로 올림
        bool showOff;
        // 맨 아래 「가이드 보기」: 쓰는 법을 한 줄에 하나씩 펼침
        bool showGuide;
        Button btnGuide;
        FrameworkElement guidePanel;
        bool boardInit;
        HashSet<string> knownIds = new HashSet<string>();
        HashSet<string> collapsedGroups = new HashSet<string>();
        Button btnOff;
        // 앱 창에서 지금 보고 있는 세션(플러그인 대표가 view.json 에 씀)
        Button btnBoardView;
        FrameworkElement foldRow;
        string viewFile;
        DateTime viewTime = DateTime.MinValue;
        string viewFocused = "";
        HashSet<string> viewVisible = new HashSet<string>();
        Dictionary<string, Border> cardById = new Dictionary<string, Border>();
        Dictionary<string, FrameworkElement> sectionById = new Dictionary<string, FrameworkElement>();
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
        Title='{TITLE}' Width='340' Height='520' MinWidth='260' MinHeight='200'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent'
        Topmost='True' ResizeMode='CanResizeWithGrip' ShowInTaskbar='True'
        FontFamily='Segoe UI, Malgun Gothic' FontSize='12' Foreground='#E8E8EA'
        TextOptions.TextFormattingMode='Display'>
  <Window.Resources>
    <FontFamily x:Key='Icons'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
    <Style TargetType='Button'>
      <Setter Property='Foreground' Value='#C8CAD1'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderBrush' Value='#3A3C44'/>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='Padding' Value='10,3'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='#2A2C33'/>
                <Setter TargetName='b' Property='BorderBrush' Value='#4A4C55'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='b' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Primary' TargetType='Button'>
      <Setter Property='Foreground' Value='#17181C'/>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='#E8E8EA' CornerRadius='6' Padding='11,4'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='#FFFFFF'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Icon' TargetType='Button'>
      <Setter Property='Foreground' Value='#8B8D96'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='FontFamily' Value='{StaticResource Icons}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Width='26' Height='26' Background='{TemplateBinding Background}' CornerRadius='6'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='#2A2C33'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='b' Property='Opacity' Value='0.35'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
          <Setter Property='Foreground' Value='#E8E8EA'/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key='Link' TargetType='Button'>
      <Setter Property='Foreground' Value='#7A7C85'/>
      <Setter Property='FontSize' Value='11'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border Background='Transparent' Padding='4,3'>
              <ContentPresenter VerticalAlignment='Center'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
          <Setter Property='Foreground' Value='#D0D2D8'/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style TargetType='ToolTip'>
      <Setter Property='Background' Value='#25272E'/>
      <Setter Property='Foreground' Value='#E8E8EA'/>
      <Setter Property='BorderBrush' Value='#3A3C44'/>
      <Setter Property='Padding' Value='8,5'/>
      <Setter Property='FontFamily' Value='Segoe UI, Malgun Gothic'/>
      <Setter Property='FontSize' Value='11.5'/>
    </Style>
    <Style TargetType='ScrollBar'>
      <Setter Property='Width' Value='8'/>
      <Setter Property='MinWidth' Value='8'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border CornerRadius='3' Background='#3A3C44' Margin='2,0'/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>
  <Border CornerRadius='12' Background='#F717181C' BorderBrush='#2C2E35' BorderThickness='1'>
    <DockPanel>
      <StackPanel x:Name='Header' DockPanel.Dock='Top' Margin='14,8,8,4' Background='Transparent'>
        <DockPanel>
          <StackPanel DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Center'>
            <Button x:Name='BtnBoardView' Style='{StaticResource Icon}' Content='&#xE890;'/>
            <Button x:Name='BtnSelect' Style='{StaticResource Icon}' Content='&#xE762;'/>
            <Border Width='1' Height='14' Background='#2E3038' Margin='3,0'/>
            <Button x:Name='BtnPin' Style='{StaticResource Icon}'/>
            <Button x:Name='BtnMin' Style='{StaticResource Icon}' Content='&#xE921;' FontSize='10'/>
            <Button x:Name='BtnClose' Style='{StaticResource Icon}' Content='&#xE8BB;' FontSize='10' ToolTip='끄기'/>
          </StackPanel>
          <TextBlock Text='세션' FontSize='14' FontWeight='SemiBold' VerticalAlignment='Center'/>
          <TextBlock x:Name='Account' Foreground='#6E7079' FontSize='11' VerticalAlignment='Center' Margin='8,1,0,0'
                     TextTrimming='CharacterEllipsis' ToolTip='Claude 앱에 로그인한 계정 · 조직'/>
        </DockPanel>
        <Grid Margin='0,8,6,0'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition/><ColumnDefinition/><ColumnDefinition/>
          </Grid.ColumnDefinitions>
          <Border x:Name='TileNeeds' Grid.Column='0' CornerRadius='8' Background='#202228' Padding='10,5,8,7' Margin='0,0,6,0'>
            <StackPanel>
              <TextBlock x:Name='StatNeeds' Text='0' FontSize='19' FontWeight='SemiBold'/>
              <TextBlock x:Name='NeedsLabel' Text='확인 필요' FontSize='11' Foreground='#8B8D96'/>
            </StackPanel>
          </Border>
          <Border Grid.Column='1' CornerRadius='8' Background='#202228' Padding='10,5,8,7' Margin='0,0,6,0'>
            <StackPanel>
              <TextBlock x:Name='StatRun' Text='0' FontSize='19' FontWeight='SemiBold'/>
              <TextBlock Text='작업 중' FontSize='11' Foreground='#8B8D96'/>
            </StackPanel>
          </Border>
          <Border Grid.Column='2' CornerRadius='8' Background='#202228' Padding='10,5,8,7'>
            <StackPanel>
              <TextBlock x:Name='StatUnread' Text='0' FontSize='19' FontWeight='SemiBold'/>
              <TextBlock Text='안 읽음' FontSize='11' Foreground='#8B8D96'/>
            </StackPanel>
          </Border>
        </Grid>
        <TextBlock x:Name='SubLine' FontSize='11' TextWrapping='Wrap' Margin='0,6,6,0' Visibility='Collapsed'/>
      </StackPanel>
      <StackPanel DockPanel.Dock='Bottom'>
        <Border x:Name='SelBar' Margin='8,4,8,2' Padding='10,7,8,4' CornerRadius='8'
                Background='#1F2A3D' BorderBrush='#3D5A8A' BorderThickness='1' Visibility='Collapsed'>
          <StackPanel>
            <TextBlock x:Name='SelCount' FontWeight='SemiBold' TextWrapping='Wrap' Margin='0,0,0,6'/>
            <WrapPanel>
              <Button x:Name='SelDone' Content='확인 끝' Margin='0,0,5,5'/>
              <Button x:Name='SelHold' Content='미루기' Margin='0,0,5,5'/>
              <Button x:Name='SelUp' Content='목록으로' ToolTip='위젯에 올리고, 미루기 · 확인 끝을 풀어 목록으로 되돌림' Margin='0,0,5,5'/>
              <Button x:Name='SelOff' Content='위젯에서 빼기' ToolTip='「안 올린 것」 칸으로' Margin='0,0,5,5'/>
              <Button x:Name='SelRead' Content='읽음으로' Margin='0,0,5,5'/>
              <Button x:Name='SelStar' Content='강조' Margin='0,0,5,5'/>
              <Button x:Name='SelAll' Content='전부 고르기' ToolTip='Ctrl+A' Margin='0,0,5,5'/>
              <Button x:Name='SelClear' Content='고르기 끝' ToolTip='Esc' Margin='0,0,5,5'/>
            </WrapPanel>
          </StackPanel>
        </Border>
        <Border x:Name='GuidePanel' Visibility='Collapsed' Margin='8,4,8,0' Padding='10,7,10,7' CornerRadius='8' Background='#202228'>
          <Grid x:Name='GuideGrid' TextBlock.FontSize='11'/>
        </Border>
        <Border Margin='0,4,0,0' BorderBrush='#24262D' BorderThickness='0,1,0,0' Padding='8,3,16,4'>
          <DockPanel>
            <StackPanel DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Center'>
              <TextBlock x:Name='Updated' Foreground='#5E606A' FontSize='11' VerticalAlignment='Center' Margin='0,0,2,0'/>
              <Button x:Name='BtnGuide' Style='{StaticResource Icon}' Content='&#xE897;'/>
            </StackPanel>
            <WrapPanel x:Name='FoldRow' VerticalAlignment='Center'>
              <Button x:Name='BtnDone' Style='{StaticResource Link}' ToolTip='확인 끝: 새 대화가 시작되거나 새 답이 오면 다시 올라옴 · 펼치기 / 접기'/>
              <Button x:Name='BtnHeld' Style='{StaticResource Link}' ToolTip='미룬 것(일단 보류): 「미루기 풀기」 할 때까지 접혀 있음 · 펼치기 / 접기'/>
              <Button x:Name='BtnOff' Style='{StaticResource Link}' ToolTip='위젯에 올리지 않은 세션: 오른쪽 클릭 「위젯에 올리기」 · 펼치기 / 접기'/>
            </WrapPanel>
          </DockPanel>
        </Border>
      </StackPanel>
      <ScrollViewer VerticalScrollBarVisibility='Auto' Margin='4,2,2,0'>
        <StackPanel x:Name='List' Margin='0,0,0,6'/>
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
            viewFile = Path.Combine(dir, "view.json");
            exePath = Assembly.GetExecutingAssembly().Location;
            settingsFile = Path.Combine(Path.GetDirectoryName(exePath), "widget-settings.json");

            Window = (Window)XamlReader.Parse(Xaml.Replace("{TITLE}", Config.Title));
            header = (FrameworkElement)Window.FindName("Header");
            tileNeeds = (Border)Window.FindName("TileNeeds");
            statNeeds = (TextBlock)Window.FindName("StatNeeds");
            statRun = (TextBlock)Window.FindName("StatRun");
            statUnread = (TextBlock)Window.FindName("StatUnread");
            needsLabel = (TextBlock)Window.FindName("NeedsLabel");
            updatedText = (TextBlock)Window.FindName("Updated");
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
            btnHeld.Click += (s, e) => { showHeld = !showHeld; SaveSettings(); Render(); if (showHeld) ShowSection("held"); };
            btnDone.Click += (s, e) => { showDone = !showDone; SaveSettings(); Render(); if (showDone) ShowSection("done"); };
            // 고정 버튼: 누를 때마다 📌 고정 → ↓📌 중간 → 고정 해제 → 📌 …
            btnPin.Click += (s, e) => SetPinMode(pinMode == "top" ? "mid" : pinMode == "mid" ? "off" : "top");
            ToolTipService.SetInitialShowDelay(btnPin, 250);
            ToolTipService.SetShowDuration(btnPin, 30000);
            ToolTipService.SetShowOnDisabled(btnMinRef = btnMin, true);
            // 고정에서는 작업 표시줄 단추를 눌러도 최소화되지 않게 되돌린다
            Window.StateChanged += (s, e) =>
            {
                if (pinMode == "top" && Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            };

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
            ((Button)Window.FindName("SelOff")).Click += (s, e) => Bulk("unboard");
            btnOff = (Button)Window.FindName("BtnOff");
            foldRow = (FrameworkElement)Window.FindName("FoldRow");
            btnBoardView = (Button)Window.FindName("BtnBoardView");
            btnBoardView.Click += (s, e) => BoardViewing();
            ToolTipService.SetShowOnDisabled(btnBoardView, true);
            btnOff.Click += (s, e) => { showOff = !showOff; SaveSettings(); Render(); if (showOff) ShowSection("off"); };
            btnGuide = (Button)Window.FindName("BtnGuide");
            guidePanel = (FrameworkElement)Window.FindName("GuidePanel");
            btnGuide.Click += (s, e) => { showGuide = !showGuide; SaveSettings(); UpdateGuide(); };
            BuildGuide();
            UpdateGuide();
            Window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && (selectMode || selected.Count > 0)) { EndSelect(); e.Handled = true; }
                else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { SelectAllMain(); e.Handled = true; }
            };
            btnMin.Click += (s, e) => { if (pinMode != "top") Window.WindowState = WindowState.Minimized; };
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
                bool shown = Window.IsVisible && Window.WindowState != WindowState.Minimized;
                if (!shown) ShowWindow();
                else if (pinMode != "top") Window.WindowState = WindowState.Minimized;
            };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) ShowWindow(); };
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
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Topmost = pinMode != "off";
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
            if (d.ContainsKey("Topmost")) pinMode = (d["Topmost"] is bool && (bool)d["Topmost"]) ? "top" : "off";
            string pm = Str(d, "pinMode");
            if (pm == "top" || pm == "mid" || pm == "off") pinMode = pm;
            if (pm == "auto") pinMode = "top"; // 11/18 잠깐 있던 판의 값
            Window.Topmost = pinMode != "off";
            showHeld = d.ContainsKey("showHeld") && d["showHeld"] is bool && (bool)d["showHeld"];
            showDone = d.ContainsKey("showDone") && d["showDone"] is bool && (bool)d["showDone"];
            showOff = d.ContainsKey("showOff") && d["showOff"] is bool && (bool)d["showOff"];
            showGuide = d.ContainsKey("showGuide") && d["showGuide"] is bool && (bool)d["showGuide"];
            boardInit = d.ContainsKey("boardInit") && d["boardInit"] is bool && (bool)d["boardInit"];
            knownIds = StrSet(Get(d, "knownIds"));
            collapsedGroups = StrSet(Get(d, "collapsedGroups"));
            openRead = StrSet(Get(d, "openReadGroups"));

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
            // 최소화 중이면 창 크기 값이 흐트러져 있으므로, 원래(되돌릴) 자리 · 크기로 저장
            var b = Window.WindowState == WindowState.Normal
                ? new Rect(Window.Left, Window.Top, Window.Width, Window.Height)
                : Window.RestoreBounds;
            if (b.IsEmpty || b.Width < Window.MinWidth || b.Height < Window.MinHeight)
                b = new Rect(Window.Left, Window.Top, Math.Max(Window.Width, 340), Math.Max(Window.Height, 520));
            d["Left"] = b.Left;
            d["Top"] = b.Top;
            d["Width"] = b.Width;
            d["Height"] = b.Height;
            d["Topmost"] = pinMode != "off";
            d["pinMode"] = pinMode;
            d["showHeld"] = showHeld;
            d["showDone"] = showDone;
            d["showOff"] = showOff;
            d["showGuide"] = showGuide;
            d["boardInit"] = boardInit;
            d["knownIds"] = knownIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            d["collapsedGroups"] = collapsedGroups.OrderBy(x => x, StringComparer.Ordinal).ToList();
            d["openReadGroups"] = openRead.OrderBy(x => x, StringComparer.Ordinal).ToList();
            WriteJson(settingsFile, d);
        }

        static HashSet<string> StrSet(object o)
        {
            var set = new HashSet<string>();
            var arr = o as IEnumerable;
            if (arr == null || o is string) return set;
            foreach (var x in arr) if (x is string) set.Add((string)x);
            return set;
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
            catch (Exception ex) { ShowStatus("표시 저장 실패: " + ex.Message, Config.ColorError); }
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
            return IsOn(m) && Str(m, "heldAt") != "" && !IsDue(m);
        }

        /// 위젯에 올린 세션인가
        static bool IsOn(Dictionary<string, object> m) { return Bool(m, "board"); }

        /// 목록 본문(섹션별)에 나오는가
        static bool InMain(Session s, Dictionary<string, object> m) { return IsOn(m) && !IsHidden(s, m); }

        /// 미룬 뒤로 새 답이 왔는가(접힌 칸에 표시만)
        static bool HeldNew(Session s, Dictionary<string, object> m)
        {
            string at = Str(m, "heldAt");
            return at != "" && !s.IsRunning && string.CompareOrdinal(s.LastActivityAt, at) > 0;
        }

        /// ✓ 확인 끝: 그 뒤로 새 활동(새 대화 · 새 답)이 없는 동안만
        static bool IsDone(Session s, Dictionary<string, object> m)
        {
            if (!IsOn(m) || Str(m, "heldAt") != "") return false;
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
            catch (Exception ex) { ShowStatus("위젯 오류: " + ex.Message, Config.ColorError); }
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
                                    Group = Str(r, "group"),
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
                                // 위젯에 올리지 않은 세션도 알리지 않는다
                                if (prevRunning.TryGetValue(s.Id, out was) && was && !s.IsRunning &&
                                    Str(MetaOf(s.Id), "heldAt") == "" && IsOn(MetaOf(s.Id)))
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

            if (File.Exists(viewFile))
            {
                var t = File.GetLastWriteTimeUtc(viewFile);
                if (t != viewTime)
                {
                    var v = ReadJson(viewFile) as Dictionary<string, object>;
                    if (v != null)
                    {
                        viewTime = t;
                        viewFocused = Str(v, "focused");
                        viewVisible = StrSet(Get(v, "visible"));
                        changed = true;
                    }
                }
            }

            if (changed && EnsureBoard()) changed = true;

            // 리마인드: 이 위젯에서 아직 안 울린 것
            foreach (var s in sessions)
            {
                var m = MetaOf(s.Id);
                if (!IsDue(m) || !IsOn(m)) continue;
                string key = s.Id + "|" + Long(m, "remindAt");
                if (alerted.Contains(key)) continue;
                alerted.Add(key);
                string memo = Str(m, "memo");
                Alert("⏰ 리마인드", s.Title + (memo != "" ? " · " + memo : ""), true);
                changed = true;
            }

            if (changed || NowMs() - lastRender > 30000) Render();
        }

        /// 처음 한 번: 지금 있는 세션을 전부 올린 것으로(화면이 그대로). 그 뒤로는 처음 보는 세션을 저절로 올림
        bool EnsureBoard()
        {
            if (sessions.Count == 0) return false;
            List<Session> add;
            if (!boardInit)
                add = sessions.ToList(); // 지금 보이는 모양 그대로(미룬 것 · 확인 끝 칸 포함) 이어 가기
            else
                add = sessions.Where(x => !knownIds.Contains(x.Id)).ToList();
            bool firstTime = !boardInit;
            foreach (var x in sessions) knownIds.Add(x.Id);
            boardInit = true;
            if (add.Count > 0)
            {
                var map = LoadMeta();
                foreach (var x in add)
                {
                    if (!map.ContainsKey(x.Id)) map[x.Id] = new Dictionary<string, object>();
                    map[x.Id]["board"] = true;
                }
                try { WriteJson(metaFile, map); meta = map; }
                catch (Exception ex) { ShowStatus("표시 저장 실패: " + ex.Message, Config.ColorError); }
            }
            if (add.Count > 0 || firstTime) SaveSettings();
            return add.Count > 0;
        }

        // ---------- 그리기 ----------

        static Brush B(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }

        // 시안 B(11/18): 바탕은 무채색, 색은 확인 필요(분홍) · 안 읽음(파랑 점) · 작업 중(주황 점) · 강조(노랑 별)에만
        const string CInk = "#E8E8EA";       // 본문 글
        const string CInkDim = "#A6A8B0";    // 읽은 세션 제목
        const string CInkMuted = "#6E7079";  // 시간 · 개수 · 흐린 아이콘
        const string CSurface = "#202228";   // 카드 · 타일 바탕
        const string CLine = "#2E3038";      // 카드 테두리
        const string CHover = "#1F2127";     // 줄에 마우스
        const string CViewingBg = "#22252E"; // 앱에서 보는 세션 줄 바탕
        const string CPickBg = "#1F2A3D";    // 고른 것
        const string CPickLine = "#8FB8FF";
        const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

        /// 윈도우 기본 아이콘 글꼴(Segoe Fluent Icons)의 글자 하나
        static TextBlock Glyph(string code, string color, double size)
        {
            var t = new TextBlock();
            t.Text = code;
            t.FontFamily = new FontFamily(IconFont);
            t.FontSize = size;
            t.Foreground = B(color);
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        /// 원격 제어 꺼짐: 사슬(연결) 아이콘에 빗금
        static FrameworkElement RemoteOffIcon()
        {
            var g = new Grid();
            g.Width = 14;
            g.Height = 14;
            g.VerticalAlignment = VerticalAlignment.Center;
            var t = Glyph("", "#6E7079", 12);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(t);
            var ln = new System.Windows.Shapes.Line();
            ln.X1 = 1; ln.Y1 = 13; ln.X2 = 13; ln.Y2 = 1;
            ln.Stroke = B("#8A8C95");
            ln.StrokeThickness = 1.3;
            g.Children.Add(ln);
            g.ToolTip = "원격 제어 꺼짐";
            return g;
        }

        static string AgoShort(long ms)
        {
            if (ms <= 0) return "";
            long min = (NowMs() - ms) / 60000;
            if (min < 1) return "방금";
            if (min < 60) return min + "분";
            long hr = min / 60;
            if (hr < 24) return hr + "시간";
            return (hr / 24) + "일";
        }

        /// 오늘이면 14:30, 아니면 11.19 09:00
        static string ClockShort(long ms)
        {
            var t = ToLocal(ms);
            var n = ToLocal(NowMs());
            return t.ToString(t.Date == n.Date ? "HH:mm" : "MM.dd HH:mm", CultureInfo.InvariantCulture);
        }

        void ShowStatus(string text, string color)
        {
            subLine.Text = text;
            subLine.Foreground = B(color);
            subLine.Visibility = text == "" ? Visibility.Collapsed : Visibility.Visible;
        }

        void Render()
        {
            long now = NowMs();
            lastRender = now;
            int heldCount = sessions.Count(s => IsHeld(s, MetaOf(s.Id)));
            int heldNewCount = sessions.Count(s => IsHeld(s, MetaOf(s.Id)) && HeldNew(s, MetaOf(s.Id)));
            int doneCount = sessions.Count(s => IsDone(s, MetaOf(s.Id)));
            int offCount = sessions.Count(s => !IsOn(MetaOf(s.Id)));
            btnDone.Content = (showDone ? "▾ " : "") + "확인 끝 " + doneCount;
            btnDone.Visibility = doneCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnHeld.Content = (showHeld ? "▾ " : "") + "미룸 " + heldCount + (heldNewCount > 0 ? " · 새 답 " + heldNewCount : "");
            btnHeld.Visibility = heldCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnOff.Content = (showOff ? "▾ " : "") + "안 올린 것 " + offCount;
            btnOff.Visibility = offCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            foldRow.Visibility = (offCount > 0 || heldCount > 0 || doneCount > 0) ? Visibility.Visible : Visibility.Collapsed;

            var viewing = sessions.FirstOrDefault(x => x.Id == viewFocused);
            if (viewing == null)
            {
                btnBoardView.ToolTip = "지금 보는 세션 — 앱 창에 세션을 띄우면 쓸 수 있습니다";
                btnBoardView.IsEnabled = false;
            }
            else
            {
                bool there = InMain(viewing, MetaOf(viewing.Id));
                btnBoardView.ToolTip = (there ? "지금 보는 세션으로 가기" : "지금 보는 세션 올리기 (미루기 · 확인 끝도 풀고 그 줄로)")
                    + "\n" + viewing.Title;
                btnBoardView.IsEnabled = true;
            }
            UpdatePinButton();

            var visible = sessions.Where(s => InMain(s, MetaOf(s.Id))).ToList();
            int running = visible.Count(s => s.IsRunning);
            int unread = visible.Count(s => IsUnread(s, MetaOf(s.Id)));
            int due = visible.Count(s => IsDue(MetaOf(s.Id)));
            int needs = visible.Count(s => IsNeeds(s));
            statNeeds.Text = needs.ToString();
            statRun.Text = running.ToString();
            statUnread.Text = unread.ToString();
            tileNeeds.Background = B(needs > 0 ? "#2A1E26" : CSurface);
            statNeeds.Foreground = B(needs > 0 ? "#FF9DBA" : CInk);
            needsLabel.Foreground = B(needs > 0 ? "#C9A3B2" : "#8B8D96");

            accountLine.Text = accountText;
            if (updatedAtMs == 0) ShowStatus(Config.MsgWaiting, Config.ColorWarn);
            else if (panelError != "") ShowStatus(panelError, Config.ColorError);
            else if (now - updatedAtMs > Config.StaleSeconds * 1000L) ShowStatus(string.Format(Config.MsgStale, AgoText(updatedAtMs)), Config.ColorWarn);
            else if (due > 0) ShowStatus("리마인드 시간이 지난 세션 " + due + "개", Config.ColorDue);
            else ShowStatus("", CInk);
            updatedText.Text = updatedAtMs > 0 ? "갱신 " + ToLocal(updatedAtMs).ToString("HH:mm", CultureInfo.InvariantCulture) : "";
            updatedText.ToolTip = "위젯에 올린 세션 " + visible.Count + "개 · 마지막 갱신 " + (updatedAtMs > 0 ? TimeText(updatedAtMs) : "없음");

            // 목록에서 사라진 세션은 고른 것에서도 뺀다
            var alive = new HashSet<string>(sessions.Select(x => x.Id));
            selected.RemoveWhere(x => !alive.Contains(x));
            UpdateSelBar();
            renderOrder = new List<string>();
            cardById = new Dictionary<string, Border>();
            sectionById = new Dictionary<string, FrameworkElement>();

            list.Children.Clear();
            var sorted = sessions
                .OrderBy(s => Rank(s, MetaOf(s.Id)))
                .ThenByDescending(s => s.LastActivityAt, StringComparer.Ordinal)
                .ToList();
            var main = sorted.Where(x => InMain(x, MetaOf(x.Id))).ToList();

            // 맨 위: 확인 필요는 섹션과 상관없이 카드로(질문 미리 보기 + 열기 · 미루기 · 확인 끝)
            var needsList = main.Where(x => IsNeeds(x)).ToList();
            if (needsList.Count > 0)
            {
                list.Children.Add(HeadLine("확인 필요", needsList.Count, null, "#C9A3B2"));
                foreach (var s in needsList) list.Children.Add(MakeNeedsCard(s, MetaOf(s.Id)));
            }

            // 그 아래: 앱 사이드바 섹션별 한 줄 목록(섹션 없음은 맨 아래). 읽은 것은 「더 보기」 로 접어 둔다
            var groups = main.Where(x => !IsNeeds(x))
                .GroupBy(x => x.Group)
                .OrderBy(g => g.Key == "" ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.CurrentCulture)
                .ToList();
            foreach (var g in groups)
            {
                string key = g.Key;
                bool folded = collapsedGroups.Contains(key);
                var head = HeadLine(key == "" ? "섹션 없음" : key, g.Count(), folded, "#9A9CA5");
                head.MouseLeftButtonUp += (o, e) =>
                {
                    if (!collapsedGroups.Remove(key)) collapsedGroups.Add(key);
                    SaveSettings();
                    Render();
                };
                list.Children.Add(head);
                if (folded) continue;
                var quiet = g.Where(x => IsQuiet(x, MetaOf(x.Id))).ToList();
                foreach (var s in g)
                    if (!quiet.Contains(s)) list.Children.Add(MakeRow(s, MetaOf(s.Id), "main"));
                if (quiet.Count > 0)
                {
                    bool open = openRead.Contains(key);
                    if (open)
                        foreach (var s in quiet) list.Children.Add(MakeRow(s, MetaOf(s.Id), "main"));
                    var more = MoreLine(open ? "읽은 것 접기" : "읽은 것 " + quiet.Count + "개 더 보기");
                    more.MouseLeftButtonUp += (o, e) =>
                    {
                        if (!openRead.Remove(key)) openRead.Add(key);
                        SaveSettings();
                        Render();
                    };
                    list.Children.Add(more);
                }
            }
            if (needsList.Count == 0 && groups.Count == 0 && updatedAtMs != 0)
                list.Children.Add(Note("위젯에 올린 세션이 없습니다. 아래 「안 올린 것」에서 올리세요."));

            if (showHeld && heldCount > 0)
            {
                var sec = HeadLine("미룬 것", heldCount, null, "#9A9CA5");
                sectionById["held"] = sec;
                list.Children.Add(sec);
                list.Children.Add(Note("「미루기 풀기」 할 때까지 여기 있습니다. 알림은 없고, 리마인드는 울립니다."));
                foreach (var s in sorted.Where(x => IsHeld(x, MetaOf(x.Id))))
                    list.Children.Add(MakeRow(s, MetaOf(s.Id), "held"));
            }
            if (showDone && doneCount > 0)
            {
                var sec = HeadLine("확인 끝", doneCount, null, "#9A9CA5");
                sectionById["done"] = sec;
                list.Children.Add(sec);
                list.Children.Add(Note("새 대화가 시작되거나 새 답이 오면 다시 올라옵니다."));
                foreach (var s in sorted.Where(x => IsDone(x, MetaOf(x.Id))))
                    list.Children.Add(MakeRow(s, MetaOf(s.Id), "done"));
            }
            if (showOff && offCount > 0)
            {
                var sec = HeadLine("안 올린 것", offCount, null, "#9A9CA5");
                sectionById["off"] = sec;
                list.Children.Add(sec);
                list.Children.Add(Note("오른쪽 클릭 「위젯에 올리기」. 여러 개 골라 한꺼번에도 됩니다."));
                foreach (var s in sessions.Where(x => !IsOn(MetaOf(x.Id)))
                                          .OrderByDescending(x => x.LastActivityAt, StringComparer.Ordinal))
                    list.Children.Add(MakeRow(s, MetaOf(s.Id), "off"));
            }
        }

        /// 「읽은 것 더 보기」 로 접어 둘 줄: 읽었고, 작업 중 · 강조 · 리마인드가 아니고, 지금 보거나 고른 것도 아님
        bool IsQuiet(Session s, Dictionary<string, object> m)
        {
            return !s.IsRunning && !IsUnread(s, m) && !Bool(m, "star") && Long(m, "remindAt") == 0
                && s.Id != viewFocused && !selected.Contains(s.Id);
        }

        /// 칸 머리 줄: 이름 · 오른쪽 개수. folded 가 있으면 접기 표시(누르면 접고 펼침)
        static Border HeadLine(string name, int count, bool? folded, string color)
        {
            var dp = new DockPanel();
            var c = new TextBlock();
            c.Text = count.ToString();
            c.Foreground = B(CInkMuted);
            c.FontSize = 11;
            DockPanel.SetDock(c, Dock.Right);
            dp.Children.Add(c);
            if (folded.HasValue)
            {
                var ch = Glyph(folded.Value ? "" : "", CInkMuted, 8);
                ch.Margin = new Thickness(0, 1, 7, 0);
                DockPanel.SetDock(ch, Dock.Left);
                dp.Children.Add(ch);
            }
            var t = new TextBlock();
            t.Text = name;
            t.Foreground = B(color);
            t.FontSize = 11;
            t.FontWeight = FontWeights.SemiBold;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            dp.Children.Add(t);
            var b = new Border();
            b.Child = dp;
            b.Background = Brushes.Transparent;
            b.Padding = new Thickness(10, 12, 10, 5);
            if (folded.HasValue) b.Cursor = Cursors.Hand;
            return b;
        }

        static Border MoreLine(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.Foreground = B(CInkMuted);
            t.FontSize = 11;
            var b = new Border();
            b.Child = t;
            b.Background = Brushes.Transparent;
            b.Padding = new Thickness(25, 4, 10, 6);
            b.Cursor = Cursors.Hand;
            b.MouseEnter += (o, e) => t.Foreground = B(CInkDim);
            b.MouseLeave += (o, e) => t.Foreground = B(CInkMuted);
            return b;
        }

        static TextBlock Note(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.TextWrapping = TextWrapping.Wrap;
            t.Foreground = B(CInkMuted);
            t.FontSize = 11;
            t.Margin = new Thickness(10, 0, 10, 4);
            return t;
        }

        /// 마우스를 올리면 보이는 자세한 글(정확한 시각 · 원격 제어 · 리마인드 · 메모)
        string RowTip(Session s, Dictionary<string, object> m)
        {
            var sb = new StringBuilder(s.Title);
            long last = IsoMs(s.LastActivityAt);
            string state = s.IsRunning ? "작업 중" : IsNeeds(s) ? "확인 필요" : IsUnread(s, m) ? "답 왔음 · 안 읽음" : "읽음";
            sb.Append("\n" + state + " · " + (last > 0 ? AgoText(last) + " (" + ClockText(last) + ")" : "시간 모름"));
            sb.Append("\n원격 제어 " + (s.Remote ? "켜짐" : "꺼짐"));
            long remindAt = Long(m, "remindAt");
            if (remindAt > 0) sb.Append("\n리마인드 " + ClockText(remindAt) + (IsDue(m) ? " (지남)" : ""));
            string memo = Str(m, "memo");
            if (memo != "") sb.Append("\n메모: " + memo);
            if (s.Id == viewFocused) sb.Append("\n앱에서 지금 보는 세션");
            return sb.ToString();
        }

        /// 제목 줄: [고르기 네모 / 상태 점] [별] 제목 … [새 답] [리마인드] [메모] [원격 꺼짐] 시간
        DockPanel TitleLine(Session s, Dictionary<string, object> m, bool card, string kind)
        {
            bool unread = IsUnread(s, m);
            bool star = Bool(m, "star");
            bool picked = selected.Contains(s.Id);
            var dp = new DockPanel();
            if (selectMode || selected.Count > 0)
            {
                var cb = Glyph(picked ? "" : "", picked ? CPickLine : CInkMuted, 12);
                cb.Margin = new Thickness(0, 0, 7, 0);
                DockPanel.SetDock(cb, Dock.Left);
                dp.Children.Add(cb);
            }
            else if (!card)
            {
                var dot = new System.Windows.Shapes.Ellipse();
                dot.Width = 7;
                dot.Height = 7;
                dot.Margin = new Thickness(1, 0, 9, 0);
                dot.VerticalAlignment = VerticalAlignment.Center;
                if (s.IsRunning) dot.Fill = B(Config.ColorRunning);
                else if (unread) dot.Fill = B(Config.ColorUnread);
                else { dot.Stroke = B("#4A4C55"); dot.StrokeThickness = 1; }
                DockPanel.SetDock(dot, Dock.Left);
                dp.Children.Add(dot);
            }
            if (star)
            {
                var st = Glyph("", Config.ColorStar, 11);
                st.Margin = new Thickness(0, 0, 5, 0);
                DockPanel.SetDock(st, Dock.Left);
                dp.Children.Add(st);
            }

            // 오른쪽은 먼저 넣은 것이 맨 오른쪽
            var time = new TextBlock();
            time.FontSize = 11;
            time.Margin = new Thickness(8, 0, 0, 0);
            time.VerticalAlignment = VerticalAlignment.Center;
            if (s.IsRunning) { time.Text = "작업 중"; time.Foreground = B(Config.ColorRunning); }
            else { time.Text = AgoShort(IsoMs(s.LastActivityAt)); time.Foreground = B(CInkMuted); }
            DockPanel.SetDock(time, Dock.Right);
            dp.Children.Add(time);
            if (!s.Remote)
            {
                var r = RemoteOffIcon();
                r.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(r, Dock.Right);
                dp.Children.Add(r);
            }
            if (!card)
            {
                string memo = Str(m, "memo");
                if (memo != "")
                {
                    var mi = Glyph("", "#8B8D96", 11);
                    mi.Margin = new Thickness(6, 0, 0, 0);
                    DockPanel.SetDock(mi, Dock.Right);
                    dp.Children.Add(mi);
                }
                long remindAt = Long(m, "remindAt");
                if (remindAt > 0)
                {
                    var al = Glyph("", IsDue(m) ? Config.ColorDue : Config.ColorRemind, 11);
                    al.Margin = new Thickness(6, 0, 0, 0);
                    DockPanel.SetDock(al, Dock.Right);
                    dp.Children.Add(al);
                }
            }
            if (kind == "held" && HeldNew(s, m))
            {
                var nw = new TextBlock();
                nw.Text = "새 답";
                nw.FontSize = 10.5;
                nw.Foreground = B(Config.ColorUnread);
                nw.Margin = new Thickness(6, 0, 0, 0);
                nw.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(nw, Dock.Right);
                dp.Children.Add(nw);
            }

            var title = new TextBlock();
            title.Text = s.Title;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.VerticalAlignment = VerticalAlignment.Center;
            bool strong = card || unread || s.IsRunning || star;
            title.Foreground = B(strong ? CInk : CInkDim);
            if (card || unread) title.FontWeight = FontWeights.SemiBold;
            dp.Children.Add(title);
            return dp;
        }

        /// 한 줄 목록. kind: main(목록) · held(미룬 것) · done(확인 끝) · off(안 올린 것)
        Border MakeRow(Session s, Dictionary<string, object> m, string kind)
        {
            renderOrder.Add(s.Id);
            bool picked = selected.Contains(s.Id);
            bool viewing = s.Id == viewFocused;
            var row = new Border();
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(9, 5, 9, 5);
            row.Margin = new Thickness(2, 0, 2, 1);
            row.Cursor = Cursors.Hand;
            row.ToolTip = RowTip(s, m);
            row.Background = picked ? B(CPickBg) : viewing ? B(CViewingBg) : Brushes.Transparent;
            if (picked)
            {
                row.BorderBrush = B(CPickLine);
                row.BorderThickness = new Thickness(1);
                row.Padding = new Thickness(8, 4, 8, 4);
            }
            if (kind != "main" && !picked) row.Opacity = kind == "held" ? 0.75 : 0.6;
            var dp = TitleLine(s, m, false, kind);
            if (viewing && !picked)
            {
                // 앱에서 보는 세션: 줄 왼쪽 끝에 가는 선
                var g = new Grid();
                var bar = new System.Windows.Shapes.Rectangle();
                bar.Width = 2;
                bar.RadiusX = 1;
                bar.RadiusY = 1;
                bar.Fill = B(Config.ColorViewing);
                bar.HorizontalAlignment = HorizontalAlignment.Left;
                bar.Margin = new Thickness(-9, 0, 0, 0);
                g.Children.Add(bar);
                g.Children.Add(dp);
                row.Child = g;
            }
            else row.Child = dp;
            if (!picked && !viewing)
            {
                row.MouseEnter += (o, e) => row.Background = B(CHover);
                row.MouseLeave += (o, e) => row.Background = Brushes.Transparent;
            }
            cardById[s.Id] = row;
            row.ContextMenu = BuildMenu(s, m, kind);
            string id = s.Id;
            row.MouseLeftButtonUp += (o, e) => OnCardClick(id);
            return row;
        }

        /// 확인 필요 카드: 제목 · 질문 미리 보기 · 리마인드 · 메모 · 열기 / 미루기 / 확인 끝
        Border MakeNeedsCard(Session s, Dictionary<string, object> m)
        {
            renderOrder.Add(s.Id);
            bool picked = selected.Contains(s.Id);
            bool viewing = s.Id == viewFocused;
            string line = picked ? CPickLine : viewing ? Config.ColorViewing : CLine;
            var card = new Border();
            card.CornerRadius = new CornerRadius(10);
            card.Background = B(picked ? CPickBg : CSurface);
            card.BorderBrush = B(line);
            card.BorderThickness = new Thickness(viewing && !picked ? 1.5 : 1);
            card.Padding = new Thickness(11, 8, 11, 9);
            card.Margin = new Thickness(4, 0, 4, 6);
            card.Cursor = Cursors.Hand;
            card.ToolTip = RowTip(s, m);
            if (!picked && !viewing)
            {
                card.MouseEnter += (o, e) => card.BorderBrush = B("#464852");
                card.MouseLeave += (o, e) => card.BorderBrush = B(CLine);
            }
            var stack = new StackPanel();
            stack.Children.Add(TitleLine(s, m, true, "main"));
            if (s.Ask != "")
            {
                var ask = new TextBlock();
                ask.Text = s.Ask;
                ask.Foreground = B("#B3B5BD");
                ask.FontSize = 11.5;
                ask.TextWrapping = TextWrapping.Wrap;
                ask.TextTrimming = TextTrimming.CharacterEllipsis;
                ask.MaxHeight = 50;
                ask.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(ask);
            }
            long remindAt = Long(m, "remindAt");
            string memo = Str(m, "memo");
            if (remindAt > 0 || memo != "")
            {
                var wp = new WrapPanel();
                wp.Margin = new Thickness(0, 5, 0, 0);
                if (remindAt > 0)
                    wp.Children.Add(IconText("", IsDue(m) ? Config.ColorDue : Config.ColorRemind,
                        (IsDue(m) ? "지남 · " : "") + ClockShort(remindAt)));
                if (memo != "") wp.Children.Add(IconText("", "#8B8D96", memo));
                stack.Children.Add(wp);
            }
            string id = s.Id;
            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.Margin = new Thickness(0, 8, 0, 0);
            btns.Children.Add(CardButton("열기", true, () => DoAction("open", id)));
            btns.Children.Add(CardButton("미루기", false, () => DoAction("hold", id)));
            btns.Children.Add(CardButton("확인 끝", false, () => DoAction("done", id)));
            stack.Children.Add(btns);
            card.Child = stack;
            cardById[s.Id] = card;
            card.ContextMenu = BuildMenu(s, m, "main");
            card.MouseLeftButtonUp += (o, e) => OnCardClick(id);
            return card;
        }

        static FrameworkElement IconText(string glyph, string color, string text)
        {
            var sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Margin = new Thickness(0, 0, 12, 0);
            var g = Glyph(glyph, color, 11);
            g.Margin = new Thickness(0, 0, 4, 0);
            sp.Children.Add(g);
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 11;
            t.Foreground = B(color == "#8B8D96" ? "#9A9CA5" : color);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.MaxWidth = 220;
            sp.Children.Add(t);
            return sp;
        }

        Button CardButton(string text, bool primary, Action act)
        {
            var b = new Button();
            b.Content = text;
            if (primary) b.Style = (Style)Window.FindResource("Primary");
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Click += (o, e) => act();
            return b;
        }

        /// 오른쪽 클릭 메뉴
        ContextMenu BuildMenu(Session s, Dictionary<string, object> m, string kind)
        {
            string id = s.Id;
            bool star = Bool(m, "star");
            bool unreadNow = IsUnread(s, m);
            long remindAt = Long(m, "remindAt");
            var menu = new ContextMenu();
            if (selected.Contains(id) && selected.Count > 1)
            {
                // 고른 것 위 오른쪽 클릭 = 고른 것 전부에
                var head = new MenuItem();
                head.Header = "고른 " + selected.Count + "개에 한꺼번에";
                head.IsEnabled = false;
                menu.Items.Add(head);
                AddItem(menu, "확인 끝", () => Bulk("done"), "");
                AddItem(menu, "미루기 (일단 보류)", () => Bulk("hold"), "");
                AddItem(menu, "목록으로 (위젯에 올리고 미루기 · 확인 끝 풀기)", () => Bulk("up"), "");
                AddItem(menu, "위젯에서 빼기", () => Bulk("unboard"), "");
                AddItem(menu, "읽음으로", () => Bulk("read"), "");
                AddItem(menu, "강조 / 해제", () => Bulk("star"), "");
                menu.Items.Add(new Separator());
                AddItem(menu, "고르기 끝", () => EndSelect());
                return menu;
            }
            AddItem(menu, "앱에서 열기", () => DoAction("open", id), "");
            if (kind == "off")
            {
                AddItem(menu, "위젯에 올리기", () => DoAction("board", id), "");
                menu.Items.Add(new Separator());
                AddItem(menu, "메모…", () => DoAction("memo", id), "");
                return menu;
            }
            AddItem(menu, star ? "강조 해제" : "강조", () => DoAction("star", id), "");
            AddItem(menu, unreadNow ? "읽음으로" : "안 읽음으로", () => DoAction("read", id), "");
            menu.Items.Add(new Separator());
            bool held = Str(m, "heldAt") != "";
            AddItem(menu, held ? "미루기 풀기" : "미루기 (일단 보류)", () => DoAction(held ? "unhold" : "hold", id), "");
            AddItem(menu, kind == "done" ? "다시 올리기" : "확인 끝", () => DoAction(kind == "done" ? "undone" : "done", id), "");
            AddItem(menu, "위젯에서 빼기", () => DoAction("unboard", id), "");
            menu.Items.Add(new Separator());
            AddItem(menu, "리마인드 30분 뒤", () => DoAction("remind30", id), "");
            AddItem(menu, "리마인드 1시간 뒤", () => DoAction("remind60", id));
            AddItem(menu, "리마인드 3시간 뒤", () => DoAction("remind180", id));
            AddItem(menu, "리마인드 내일 " + Config.TomorrowHour + "시", () => DoAction("remindTomorrow", id));
            if (remindAt > 0) AddItem(menu, "리마인드 끄기", () => DoAction("remindOff", id));
            menu.Items.Add(new Separator());
            AddItem(menu, "메모…", () => DoAction("memo", id), "");
            return menu;
        }

        /// 펼친 칸의 머리 줄로 스크롤(그리기가 끝난 뒤)
        void ShowSection(string key)
        {
            FrameworkElement el;
            if (!sectionById.TryGetValue(key, out el)) return;
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                el.BringIntoView(new Rect(0, 0, el.ActualWidth, Math.Max(el.ActualHeight, 400)));
            }), DispatcherPriority.Loaded);
        }

        /// 앱에서 보고 있는 세션을 위젯에 올리고(미루기 · 확인 끝도 풀고) 그 카드로 스크롤
        void BoardViewing()
        {
            var s = sessions.FirstOrDefault(x => x.Id == viewFocused);
            if (s == null) return;
            string id = s.Id;
            collapsedGroups.Remove(s.Group);
            openRead.Add(s.Group);
            if (!InMain(s, MetaOf(id)))
                ChangeMeta(id, m => { m["board"] = true; m["heldAt"] = null; m["hiddenAt"] = null; });
            SaveSettings();
            Render();
            Border card;
            if (cardById.TryGetValue(id, out card)) card.BringIntoView();
        }

        // ---------- 고정 ----------

        /// 맨 아래 가이드: 동작 하나당 한 줄 + 아이콘 뜻
        void BuildGuide()
        {
            var grid = (Grid)Window.FindName("GuideGrid");
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            string[,] rows = {
                { "줄 · 카드 클릭", "앱에서 그 세션 열기 + 읽음 표시" },
                { "오른쪽 클릭", "강조 · 미루기 · 확인 끝 · 위젯에서 빼기 · 리마인드 · 메모" },
                { "확인 필요 카드", "열기 · 미루기(일단 보류) · 확인 끝 버튼" },
                { "섹션 이름 클릭", "그 섹션 접기 · 펼치기" },
                { "읽은 것 더 보기", "읽은 세션 펼치기 · 접기" },
                { "Ctrl + 클릭", "하나씩 고르기 · 풀기" },
                { "Shift + 클릭", "지난번 고른 것부터 여기까지 전부 고르기" },
                { "Ctrl + A / Esc", "목록 전부 고르기 / 고르기 끝" },
                { "고정 버튼", "누를 때마다 고정 → 중간 → 고정 해제" },
                { "최소화 버튼", "작업 표시줄로 내리기(고정 중에는 꺼짐)" },
                { "트레이 아이콘", "더블클릭으로 다시 띄우기 · 오른쪽 클릭 메뉴" },
            };
            int r = 0;
            for (int i = 0; i < rows.GetLength(0); i++, r++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var a = new TextBlock();
                a.Text = rows[i, 0];
                a.Foreground = B(CInk);
                a.Margin = new Thickness(0, 1, 12, 1);
                Grid.SetRow(a, r);
                grid.Children.Add(a);
                var b = new TextBlock();
                b.Text = rows[i, 1];
                b.Foreground = B(CInkDim);
                b.TextWrapping = TextWrapping.Wrap;
                b.Margin = new Thickness(0, 1, 0, 1);
                Grid.SetRow(b, r);
                Grid.SetColumn(b, 1);
                grid.Children.Add(b);
            }
            // 아이콘 뜻
            var icons = new List<KeyValuePair<FrameworkElement, string>>();
            icons.Add(new KeyValuePair<FrameworkElement, string>(Glyph("", Config.ColorStar, 11), "강조"));
            icons.Add(new KeyValuePair<FrameworkElement, string>(Glyph("", Config.ColorRemind, 11), "리마인드 (빨강 = 시간 지남)"));
            icons.Add(new KeyValuePair<FrameworkElement, string>(Glyph("", "#8B8D96", 11), "메모"));
            icons.Add(new KeyValuePair<FrameworkElement, string>(RemoteOffIcon(), "원격 제어 꺼짐"));
            bool first = true;
            foreach (var kv in icons)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var el = kv.Key;
                el.HorizontalAlignment = HorizontalAlignment.Left;
                el.Margin = new Thickness(2, first ? 7 : 1, 0, 1);
                Grid.SetRow(el, r);
                grid.Children.Add(el);
                var b = new TextBlock();
                b.Text = kv.Value;
                b.Foreground = B(CInkDim);
                b.Margin = new Thickness(0, first ? 7 : 1, 0, 1);
                Grid.SetRow(b, r);
                Grid.SetColumn(b, 1);
                grid.Children.Add(b);
                first = false;
                r++;
            }
        }

        void UpdateGuide()
        {
            guidePanel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
            btnGuide.Background = showGuide ? B("#2A2C33") : Brushes.Transparent;
            btnGuide.Foreground = B(showGuide ? CInk : "#8B8D96");
            btnGuide.ToolTip = showGuide ? "가이드 감추기" : "가이드 보기 — 위젯 쓰는 법";
        }

        void UpdatePinButton()
        {
            const string cycle = "\n\n누를 때마다:  고정  →  중간  →  고정 해제  →  고정 …";
            if (pinMode == "off")
            {
                btnPin.Content = Glyph("", "#8B8D96", 13);
                btnPin.Background = Brushes.Transparent;
                btnPin.ToolTip = "고정 해제\n보통 창입니다. 위젯 밖을 누르면 그 창 뒤로 내려가고, 직접 띄우기 전까지는 안 보입니다.\n(작업 표시줄 · 트레이 아이콘으로 띄움)\n\n· 누르면 → 고정" + cycle;
            }
            else if (pinMode == "mid")
            {
                // 핀 오른쪽 아래에 작은 아래 화살표
                var g = new Grid();
                g.Children.Add(Glyph("", CInk, 13));
                var ch = Glyph("", CInk, 7);
                ch.HorizontalAlignment = HorizontalAlignment.Right;
                ch.VerticalAlignment = VerticalAlignment.Bottom;
                ch.Margin = new Thickness(0, 0, -6, -3);
                g.Children.Add(ch);
                btnPin.Content = g;
                btnPin.Background = B("#262830");
                btnPin.ToolTip = "중간\n위젯 밖을 눌러도 내려가지 않고 늘 위에 있습니다.\n— 최소화 버튼을 누를 때만 내려가고, 작업 표시줄 단추나 트레이 아이콘(더블클릭)으로 다시 띄웁니다.\n\n· 누르면 → 고정 해제" + cycle;
            }
            else
            {
                btnPin.Content = Glyph("", CInk, 13);
                btnPin.Background = B("#262830");
                btnPin.ToolTip = "고정\n늘 맨 위에 있고, 어디를 눌러도 내려가지 않습니다(최소화 버튼도 막힘).\n\n· 누르면 → 중간(최소화 버튼으로만 내릴 수 있음)" + cycle;
            }
            if (btnMinRef != null)
            {
                btnMinRef.IsEnabled = pinMode != "top";
                btnMinRef.ToolTip = pinMode == "top"
                    ? "고정 중에는 내릴 수 없습니다 — 고정 버튼을 눌러 중간으로 바꾸면 내릴 수 있습니다"
                    : "최소화 — 작업 표시줄 단추나 트레이 아이콘(더블클릭)으로 다시 띄웁니다";
            }
        }

        void SetPinMode(string mode)
        {
            pinMode = mode;
            Window.Topmost = mode != "off";
            SaveSettings();
            Render();
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
                if (InMain(s, MetaOf(s.Id))) selected.Add(s.Id);
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
            btnSelect.Background = on ? B("#2F3340") : Brushes.Transparent;
            btnSelect.Foreground = B(on ? CPickLine : "#8B8D96");
            btnSelect.ToolTip = on
                ? "고르는 중 — 누르면 고르기 끝 (Esc)"
                : "여러 개 고르기 — 켜면 줄을 누를 때마다 골라짐 · 꺼져 있어도 Ctrl 클릭 · Shift 클릭으로 고를 수 있음";
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
                    case "up": m["board"] = true; m.Remove("heldAt"); m.Remove("hiddenAt"); break;
                    case "unboard": m.Remove("board"); break;
                    case "read": if (!s.IsRunning) m["seenAt"] = s.LastActivityAt; break;
                    case "star": if (starAll) m["star"] = true; else m.Remove("star"); break;
                }
            }
            try { WriteJson(metaFile, map); }
            catch (Exception ex) { ShowStatus("표시 저장 실패: " + ex.Message, Config.ColorError); return; }
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

        static void AddItem(ContextMenu menu, string header, Action act, string glyph = null)
        {
            var mi = new MenuItem();
            mi.Header = header;
            if (glyph != null) mi.Icon = Glyph(glyph, "#4A4D55", 12);
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
                    viewFocused = id;
                    if (!s.IsRunning) ChangeMeta(id, m => { m["seenAt"] = iso; });
                    else Render();
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
                case "board":
                    ChangeMeta(id, m => { m["board"] = true; m["heldAt"] = null; m["hiddenAt"] = null; });
                    break;
                case "unboard":
                    ChangeMeta(id, m => { m["board"] = null; });
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
