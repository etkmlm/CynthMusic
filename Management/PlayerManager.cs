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
using System.Timers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CynthMusic.Management
{
    public class PlayerManager
    {
        private readonly MediaElement player;
        private readonly Action PreparePlay;
        private readonly Action<string> setIconTip;
        private PlayerService service => MainWindow.playerService;
        protected bool isPlaying = false;
        public bool isLoop = false;
        public bool isListLoop = false;
        public bool isShuffled = false;

        protected int[] playingListOrders;
        private IMusic playingMedia;
        private Timer timerPosition;

        public bool IsPlayingList => PlayerService.PlayingListID != -1;
        public bool IsListAvailable => IsPlayingList && service.srcPlaying.Count != 0;

        public PlayerManager(ref MediaElement player, Action progressChanged, Action prepare, Action mediaFinish, Action<string> setState, Action<string> setIconTip)
        {
            this.player = player;
            this.setIconTip = setIconTip;
            player.MediaEnded += (a, b) =>
            {
                if (isLoop)
                    Play(playingMedia);
                else if (IsListAvailable)
                {
                    if (!Next())
                        mediaFinish.Invoke();
                }
                else
                {
                    Stop();
                    mediaFinish.Invoke();
                }
            };

            PreparePlay = () =>
            {
                isPlaying = false;
                timerPosition = new Timer();
                timerPosition.Elapsed += (c, d) => progressChanged.Invoke();
                timerPosition.Interval = 750;
                timerPosition.Start();
                prepare.Invoke();
                setState(playingMedia.Name);
            };
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
        public bool TogglePlay()
        {
            if (player.Source == null || !player.Source.IsAbsoluteUri)
                return false;
            if (isPlaying)
                player.Pause();
            else
                player.Play();
            isPlaying = !isPlaying;
            return isPlaying;
        }
        
        #region Player
        public async void Play(IMusic music, int index = -1, TimeSpan? position = null)
        {
            if (string.IsNullOrEmpty(music.PlayURL))
            {
                if (music is YouTubeMusic)
                    await service.PlayMusicWithLoad(service.srcPlaying.FirstOrDefault(x => x.Item.Music.Equals(music)));
                else
                    Stop();
                return;
            }
            player.Stop();
            player.Source = new Uri(music.PlayURL);
            setIconTip("Oynatılıyor: " + music.Name);
            player.Play();
            if (index != -1)
                music.ID = index;
            playingMedia = music;
            PreparePlay();
            if (position.HasValue)
                player.Position = position.Value;
        }
        public void ConvertPlay(Orderable<ColorableMusic> music, TimeSpan? position = null) =>
            Play(music.Item.Music, music.Index, position);
        public void Play(string identity, TimeSpan? position = null) =>
            ConvertPlay(service.srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == identity), position);
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
            (System.Windows.Application.Current.MainWindow as MainWindow).CancelToken();
            service.srcPlaying.Clear();
            PlayerService.PlayingListID = -1;
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
