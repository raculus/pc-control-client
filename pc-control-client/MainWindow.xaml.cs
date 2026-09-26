using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using Windows.Storage;
using WinRT.Interop;

namespace pc_control_client
{
    public sealed partial class MainWindow : Window
    {
        private readonly StatusMonitorService _monitorService;
        private AppWindow _appWindow;

        public MainWindow()
        {
            InitializeComponent();

            SetWindowPositionAndSize((int)RootGrid.Width, (int)RootGrid.Height);

            _monitorService = new StatusMonitorService();
            _monitorService.StatusUpdated += OnStatusUpdated;

            // 설정 필드 초기값 바인딩
            TxtApiUrl.Text = _monitorService.BaseApiUrl;

            UpdateAlertSettings();
            _monitorService.Start();
        }

        private void SetWindowPositionAndSize(int width, int height)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            // 작업 표시줄 및 제목 표시줄 아이콘 직접 설정
            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            }

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = false;
            }

            DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int margin = 20;
            int posX = workArea.X + workArea.Width - width - margin;
            int posY = workArea.Y + margin;

            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(posX, posY, width, height));
        }

        private void OnStatusUpdated(ClientStatusResponse data)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TxtClientName.Text = $"{data.Name}님 사용시간";

                if (DateTime.TryParse(data.LastBootedAt, out DateTime bootTime))
                {
                    TimeSpan uptime = DateTime.Now - bootTime;
                    if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;

                    if (uptime.TotalHours >= 1)
                        TxtUptime.Text = $"{(int)uptime.TotalHours}시 {uptime.Minutes}분";
                    else
                        TxtUptime.Text = $"{uptime.Minutes}분";
                }
                else
                {
                    TxtUptime.Text = "-";
                }

                TimeSpan remaining = TimeSpan.FromSeconds(data.RemainingSeconds);
                TxtRemainingTime.Text = $"{remaining.Hours:D2}시 {remaining.Minutes:D2}분";
            });
        }

        private void BtnSaveApiUrl_Click(object sender, RoutedEventArgs e)
        {
            string newUrl = TxtApiUrl.Text.Trim();
            if (!string.IsNullOrEmpty(newUrl))
            {
                _monitorService.UpdateBaseApiUrl(newUrl);
                ApiSettingsFlyout.Hide();
            }
        }

        private void OnAlertSettingChanged(object sender, RoutedEventArgs e)
        {
            UpdateAlertSettings();
        }

        private void UpdateAlertSettings()
        {
            if (_monitorService == null) return;

            _monitorService.Enable30Min = Chk30Min.IsChecked ?? false;
            _monitorService.Enable15Min = Chk15Min.IsChecked ?? false;
            _monitorService.Enable10Min = Chk10Min.IsChecked ?? false;
            _monitorService.Enable5Min = Chk5Min.IsChecked ?? false;
        }

        private void SldVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int volumePercent = (int)e.NewValue;

            if (TxtVolumeValue != null)
            {
                TxtVolumeValue.Text = $"{volumePercent}%";
            }

            _monitorService?.SetVolume(volumePercent);
        }

        private void BtnTestAudio_Click(object sender, RoutedEventArgs e)
        {
            _monitorService?.TestNotification("30분", "30분.mp3");
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            args.Handled = true;
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }
    }

    public class StatusMonitorService
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly AudioQueuePlayer _audioPlayer = new AudioQueuePlayer();

        public string TargetMac { get; private set; }
        public string BaseApiUrl { get; private set; }

        public bool Enable60Min { get; set; } = true;
        public bool Enable30Min { get; set; } = true;
        public bool Enable15Min { get; set; } = true;
        public bool Enable10Min { get; set; } = true;
        public bool Enable5Min { get; set; } = true;

        private readonly Dictionary<int, bool> _notifiedMap = new Dictionary<int, bool>
        {
            { 3600, false },
            { 1800, false },
            { 900,  false },
            { 600,  false },
            { 300,  false }
        };

        public event Action<ClientStatusResponse>? StatusUpdated;

        public StatusMonitorService()
        {
            TargetMac = GetLocalMacAddress();

            var localSettings = ApplicationData.Current.LocalSettings;
            BaseApiUrl = localSettings.Values["BaseApiUrl"] as string ?? "http://localhost:3000";

            _timer.Interval = TimeSpan.FromSeconds(30);
            _timer.Tick += async (s, e) => await FetchStatusAsync();
        }

        private string GetLocalMacAddress()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                // 1. 디버깅용: 현재 인식된 모든 네트워크 인터페이스 출력
                foreach (var adapter in interfaces)
                {
                    SimpleLogger.Log($"[NIC 디버그] 이름: {adapter.Name} | 타입: {adapter.NetworkInterfaceType} | 상태: {adapter.OperationalStatus} | 설명: {adapter.Description}");
                }

                // 2. 물리적 이더넷 또는 Wi-Fi 어댑터 추출 (조건 완화)
                var nic = interfaces.FirstOrDefault(i =>
                    i.OperationalStatus == OperationalStatus.Up &&
                    (i.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                    i.GetPhysicalAddress().GetAddressBytes().Length == 6); // MAC 주소가 정상 길이(6바이트)인 경우

                // 3. 만약 위 조건으로 안 잡히면 루프백이 아닌 아무 활성 어댑터나 선택
                nic ??= interfaces.FirstOrDefault(i =>
                    i.OperationalStatus == OperationalStatus.Up &&
                    i.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    i.GetPhysicalAddress().GetAddressBytes().Length == 6);

                if (nic != null)
                {
                    byte[] bytes = nic.GetPhysicalAddress().GetAddressBytes();
                    string mac = string.Join(":", bytes.Select(b => b.ToString("x2")));
                    SimpleLogger.Log($"[추출 성공] 연결된 NIC: {nic.Name} ({nic.Description}), MAC: {mac}");
                    return mac;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.LogError("[MAC 추출 실패 예외 발생]", ex);
                throw ex;
            }

            SimpleLogger.LogError("[MAC 추출 실패] 조건에 맞는 어댑터를 찾지 못해 기본값 사용");
            return "d8:bb:c1:cd:2d:50";
        }

        public void UpdateBaseApiUrl(string newBaseUrl)
        {
            BaseApiUrl = newBaseUrl.TrimEnd('/');

            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["BaseApiUrl"] = BaseApiUrl;

            _ = FetchStatusAsync();
        }

        public void Start()
        {
            _timer.Start();
            _ = FetchStatusAsync();
        }

        public void SetVolume(int volumePercent)
        {
            _audioPlayer.SetVolume(volumePercent);
        }

        private async System.Threading.Tasks.Task FetchStatusAsync()
        {
            try
            {
                string fullUrl = $"{BaseApiUrl}/api/client/status-by-mac?mac={TargetMac}";
                SimpleLogger.Log($"Fetching status from: {fullUrl}");

                // Source Generator Context의 TypeInfo 지정
                var data = await _httpClient.GetFromJsonAsync(
                    fullUrl,
                    AppJsonContext.Default.ClientStatusResponse
                );

                if (data == null) return;

                StatusUpdated?.Invoke(data);
                CheckAndNotify(data.RemainingSeconds);
            }
            catch (Exception ex)
            {
                SimpleLogger.LogError("Error occurred while fetching status", ex);
            }
        }

        private void CheckAndNotify(int remainingSeconds)
        {
            ResetFlagsIfRecharged(remainingSeconds);

            CheckThreshold(remainingSeconds, 3600, Enable60Min, "1시간", "1시간.mp3");
            CheckThreshold(remainingSeconds, 1800, Enable30Min, "30분", "30분.mp3");
            CheckThreshold(remainingSeconds, 900, Enable15Min, "15분", "15분.mp3");
            CheckThreshold(remainingSeconds, 600, Enable10Min, "10분", "10분.mp3");
            CheckThreshold(remainingSeconds, 300, Enable5Min, "5분", "5분.mp3");
        }

        private void CheckThreshold(int currentSec, int thresholdSec, bool isEnabled, string timeLabel, string timeAudioFile)
        {
            if (!isEnabled) return;

            if (currentSec <= thresholdSec && !_notifiedMap[thresholdSec])
            {
                _notifiedMap[thresholdSec] = true;
                Notification(timeLabel, timeAudioFile);
            }
        }

        public void TestNotification(string timeLabel, string timeAudioFile)
        {
            Notification(timeLabel, timeAudioFile);
        }

        public void Notification(string timeLabel, string timeAudioFile)
        {
            ShowToastNotification("PC 사용시간", $"남은 시간이 {timeLabel} 남았습니다.");
            _audioPlayer.PlaySequence(new[]
            {
                "남은시간.mp3",
                timeAudioFile,
                "남았습니다.mp3"
            });
        }

        private void ResetFlagsIfRecharged(int currentSec)
        {
            var keys = new List<int>(_notifiedMap.Keys);
            foreach (var key in keys)
            {
                if (currentSec > key + 30)
                {
                    _notifiedMap[key] = false;
                }
            }
        }

        private void ShowToastNotification(string title, string content)
        {
            try
            {
                var toast = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(content)
                    .BuildNotification();

                AppNotificationManager.Default.Show(toast);
            }
            catch (Exception ex)
            {
                SimpleLogger.LogError("Error occurred while showing toast notification", ex);
            }
        }
    }
}