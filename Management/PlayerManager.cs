/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore;
using CynthCore.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CynthMusic.Management
{
    public class PlayerManager
    {
        private readonly MediaElement player;
        private PlayerService service => MainWindow.playerService;
        private MainWindow window = (MainWindow)App.Current.MainWindow;
        private int retryCount = 5;
        protected bool isPlaying = false;
        public bool isLoop = false;
        public bool isListLoop = false;
        public bool isShuffled = false;

        protected int[] playingListOrders;
        private IMusic playingMedia;

        private TimeSpan? savedPosition;

        private DispatcherTimer timerPosition;

        public bool IsPlayingList => PlayerService.PlayingListID != -1;
        public bool IsListAvailable => IsPlayingList && service.srcPlaying.Count != 0;

        public PlayerManager(ref MediaElement player)
        {
            player.MediaFailed += async (a, b) => await RetryMedia();
            player.MediaOpened += (a, b) => retryCount = 5;
            player.MediaEnded += async (a, b) =>
            {
                if (this.player.Position.TotalSeconds == 0 || !this.player.NaturalDuration.HasTimeSpan || this.player.NaturalDuration.TimeSpan.TotalSeconds == 0)
                {
                    await RetryMedia();
                    return;
                }
                if (savedPosition != null)
                {
                    await Play(playingMedia, -1, savedPosition);
                    savedPosition = null;
                    return;
                }
                if (isLoop)
                    await Play(playingMedia);
                else if (IsListAvailable)
                {
                    if (!Next())
                        StopGUI();
                }
                else
                    Stop();
            };
            this.player = player;
        }

        public async Task RetryMedia()
        {
            if (retryCount == 0)
            {
                retryCount = 5;
                if (IsListAvailable)
                    Next();
                else
                    Stop();
            }
            await service.PlayMusicWithLoad(service.srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == playingMedia.SaveIdentity));
            retryCount--;
        }

        public void PlayList()
        {
            if (isPlaying)
                Stop();
            ConvertPlay(service.srcPlaying[0]);
        }
        public void PlayFirst()
        {
            if (isShuffled)
                ConvertPlay(service.srcPlaying[playingListOrders[0] - 1]);
            else
                ConvertPlay(service.srcPlaying[0]);
        }
        public void TogglePlay(bool state)
        {
            if (player.Source == null || !player.Source.IsAbsoluteUri)
                return;
            if (state)
                player.Play();
            else
                player.Pause();
            isPlaying = state;
        }
        public bool TogglePlay()
        {
            TogglePlay(!isPlaying);
            return isPlaying;
        }

        #region Player
        public async Task Play(IMusic music, int index = -1, TimeSpan? position = null, bool applyMedia = true)
        {
            if (string.IsNullOrEmpty(music.PlayURL))
            {
                if (music is YouTubeMusic)
                    await service.PlayMusicWithLoad(service.srcPlaying.FirstOrDefault(x => x.Item.Music.Equals(music)), position, applyMedia);
                else
                    Stop();
                return;
            }

            window.sldPosition.Value = 0;
            player.Stop();
            player.Source = new Uri(music.PlayURL);
            player.Play();
            if (index != -1)
                music.ID = index;
            if (applyMedia)
                playingMedia = music;

            timerPosition = new DispatcherTimer();
            timerPosition.Tick += PositionTimer_Tick;
            timerPosition.Interval = TimeSpan.FromMilliseconds(750);
            timerPosition.Start();
            window.SetPlayState(true);
            isPlaying = true;
            window.lblState.Content = music.Name;
            window.lblStateContext.Content = music.Name;

            if (position.HasValue)
                player.Position = position.Value;
        }
        public void PlayTemp(Orderable<ColorableMusic> music)
        {
            savedPosition = player.Position;
            ConvertPlay(music, applyMedia: false);
        }
        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (player.BufferingProgress is not 0 and not 1)
            {
                window.sldPosition.Foreground = new SolidColorBrush(Color.FromRgb(200, 170, 20));
                window.sldPosition.Maximum = 100;
                var progress = player.BufferingProgress * 100;
                window.sldPosition.Value = progress;
                window.lblPosition.Content = progress + " / 100";
            }
            else
            {
                window.sldPosition.Foreground = new SolidColorBrush(Color.FromRgb(13, 127, 100));
                if (!player.NaturalDuration.HasTimeSpan)
                {
                    window.sldPosition.Maximum = 0;
                    return;
                }
                window.sldPosition.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds;
                window.sldPosition.Value = player.Position.TotalSeconds;
                window.lblPosition.Content = player.Position.ToString(@"mm\.ss") + " / " + player.NaturalDuration.TimeSpan.ToString(@"mm\.ss");
            }
        }
        public async void ConvertPlay(Orderable<ColorableMusic> music, TimeSpan? position = null, bool applyMedia = true) =>
            await Play(music.Item.Music, music.Index, position, applyMedia);
        public void Play(string identity, TimeSpan? position = null, bool applyMedia = true) =>
            ConvertPlay(service.srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == identity), position, applyMedia);
        public void StopGUI()
        {
            window.SetPlayState(false);
            window.sldPosition.Value = 0;
            window.lblState.Content = "Boş";
            window.lblStateContext.Content = "Boş";
            window.lblPosition.Content = "0.00 / 0.00";
            window.btnShuffle.Foreground = new SolidColorBrush(Colors.White);
        }
        public void Stop()
        {
            player.Stop();
            if (timerPosition != null)
                timerPosition.Stop();
            player.Source = null;
            playingMedia = null;
            playingListOrders = null;
            isShuffled = false;
            isPlaying = false;
            service.CancelToken();
            service.srcPlaying.Clear();
            PlayerService.PlayingListID = -1;
            StopGUI();
        }
        public bool Next()
        {
            if (!IsListAvailable)
                return false;
            int index = playingMedia == null ? 0 : playingMedia.ID - 1;
            if (isShuffled)
                return ShuffledNext(index);
            else
            {
                if (index == service.srcPlaying.Count - 1)
                {
                    if (isListLoop)
                        ConvertPlay(service.srcPlaying[0]);
                    else
                        return false;
                }
                else
                    ConvertPlay(service.srcPlaying[index + 1]);
            }
            return true;
        }
        public void Previous()
        {
            if (!IsListAvailable)
                return;
            int index = playingMedia.ID - 1;
            if (isShuffled)
                ShuffledPrevious(index);
            else if (index != 0)
                ConvertPlay(service.srcPlaying[index - 1]);
        }
        public void Shuffle(bool mode, IEnumerable<int> shuffle = null)
        {
            var actual = GetActualOrder();
            playingListOrders = mode ? (shuffle ?? ShuffleList(actual)).ToArray() : actual;
            isShuffled = mode;
        }
        #endregion

        #region Utils
        private bool ShuffledNext(int index)
        {
            int ix = Array.IndexOf(playingListOrders, index + 1);
            if (ix == service.srcPlaying.Count - 1)
            {
                if (isListLoop)
                    ConvertPlay(service.srcPlaying[playingListOrders[0] - 1]);
                else
                    return false;
            }
            else
            {
                try { ConvertPlay(service.srcPlaying[playingListOrders[ix + 1] - 1]); }
                catch (ArgumentOutOfRangeException) { ConvertPlay(service.srcPlaying[playingListOrders[^1] - 1]); }
            }
            return true;
        }
        private void ShuffledPrevious(int index)
        {
            int ix = Array.IndexOf(playingListOrders, index + 1);
            if (ix != 0)
                ConvertPlay(service.srcPlaying[playingListOrders[ix - 1] - 1]);
        }
        public int[] GetActualOrder()
        {
            int length = service.srcPlaying.Count;
            int[] order = new int[length];
            for (int i = 0; i < length; i++)
                order[i] = service.srcPlaying[i].Index;
            return order;
        }
        public int[] ShuffleList(int[] actual)
        {
            Random rnd = new Random();
            int length = actual.Length;
            int[] randomized = new int[length];
            for (int i = 0; i < length; i++)
            {
                int placeIndex;
                do
                    placeIndex = rnd.Next(length);
                while (randomized[placeIndex] != 0);
                randomized[placeIndex] = actual[i];
            }
            return randomized;
        }
        public void ApplyExchange()
        {
            if (!IsPlayingList)
                return;
            playingMedia.ID = service.srcPlaying.FirstOrDefault(x => playingMedia.Equals(x.Item.Music)).Index;
        }
        public void SetPosition(double seconds) => 
            player.Position = TimeSpan.FromSeconds(seconds);
        public int? GetPlayingID() =>
            playingMedia?.ID;
        protected string GetPosition() =>
            player.Position.ToString(@"hh\.mm\.ss");
        #endregion
    }
}
