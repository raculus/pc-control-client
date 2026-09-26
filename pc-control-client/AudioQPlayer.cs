using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace pc_control_client
{
    public class AudioQueuePlayer
    {
        private readonly MediaPlayer _mediaPlayer = new MediaPlayer();
        private readonly Queue<string> _playQueue = new Queue<string>();

        public AudioQueuePlayer()
        {
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        }

        public void PlaySequence(IEnumerable<string> fileNames)
        {
            lock (_playQueue)
            {
                _playQueue.Clear();
                foreach (var name in fileNames)
                {
                    _playQueue.Enqueue(name);
                }
            }

            // 비동기 재생 시작
            _ = PlayNextAsync();
        }
        // 볼륨 변경 (0 ~ 100 정수입력 -> 0.0 ~ 1.0 변환)
        public void SetVolume(int volumePercent)
        {
            double volume = Math.Clamp(volumePercent / 100.0, 0.0, 1.0);
            _mediaPlayer.Volume = volume;
        }

        private async System.Threading.Tasks.Task PlayNextAsync()
        {
            string? nextFile = null;
            lock (_playQueue)
            {
                if (_playQueue.Count > 0)
                {
                    nextFile = _playQueue.Dequeue();
                }
            }

            if (nextFile != null)
            {
                try
                {
                    // 패키지 리소스 URI 생성
                    Uri uri = new Uri($"ms-appx:///Assets/Sounds/{nextFile}");

                    // StorageFile 객체로 패키지 내부 파일 가져오기
                    var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);

                    if (file != null)
                    {
                        Debug.WriteLine($"[음성 재생 성공] {file.Path}");
                        _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
                        _mediaPlayer.Play();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[음성 재생 오류] {nextFile}: {ex.Message}");
                    // 에러 발생 시 다음 파일로 연속 진행
                    _ = PlayNextAsync();
                }
            }
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            // 이전 음성이 끝나면 다음 음성 비동기 재생
            _ = PlayNextAsync();
        }
    }
}
