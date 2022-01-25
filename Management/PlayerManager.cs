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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CynthMusic.Management
{
    public delegate void PositionChanged(TimeSpan pos, Duration duration, double buff);
    public delegate void MediaChanged(IMusic media, bool autoPlay);
    public delegate void MediaEnd(int index);
    public delegate void MediaFail(Exception e);
    public class PlayerManager
    {
        private readonly MediaElement player;
        public string Position => player.Position.ToString(@"hh\.mm\.ss");
        protected Shuffler<Orderable<ColorableMusic>> shuffler;
        public OrderableCollection<ColorableMusic> srcPlaying;
        protected event PositionChanged PositionChanged;
        protected event MediaChanged MediaChanged;
        protected event MediaEnd MediaEnd;
        protected event MediaFail MediaFail;
        private DispatcherTimer timer;

        public IMusic playingMusic;
        public bool isShuffled = false;
        public bool isListLoop = false;
        public bool isLoop = false;
        public bool isPlaying = false;
        public bool isLoaded = false;
        protected int retry = 5;

        public PlayerManager(ref MediaElement player)
        {
            this.player = player;
            ResetShuffler();
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            timer.Tick += (a, b) => PositionChanged.Invoke(this.player.Position, this.player.NaturalDuration, this.player.BufferingProgress);

            player.MediaEnded += (a, b) => MediaEnd.Invoke(GetMediaIndex());

            player.MediaFailed += (a, b) => MediaFail.Invoke(b.ErrorException);

            player.MediaOpened += (a, b) => retry = 5;
        }

        protected void Play(IMusic music, TimeSpan? position = null, bool autoPlay = true)
        {
            playingMusic = music;
            player.Stop();
            player.Source = new Uri(music.PlayURL);
            player.Position = position ?? TimeSpan.Zero;
            MediaChanged.Invoke(music, autoPlay);
            if (!timer.IsEnabled)
                timer.Start();
            if (autoPlay)
                player.Play();
            isPlaying = autoPlay;
            isLoaded = true;
        }

        public void Resume()
        {
            timer.Start();
            player.Play();
            isPlaying = true;
        }

        protected void Pause()
        {
            timer.Stop();
            player.Pause();
            isPlaying = false;
        }

        protected void Stop()
        {
            player.Stop();
            playingMusic = null;
            isPlaying = false;
            isShuffled = false;
            isLoaded = true;
            timer.Stop();
        }

        protected void ResetShuffler() =>
            shuffler = new Shuffler<Orderable<ColorableMusic>>(srcPlaying);
        
        protected string ShufflerToString() =>
            string.Join(',', shuffler.Select(x => x.Index - 1));

        protected Orderable<ColorableMusic> Next(int? nowIndex = null) =>
            srcPlaying[GetNextMediaIndex(nowIndex)];

        protected Orderable<ColorableMusic> Previous(int? nowIndex = null) =>
            srcPlaying[GetPreviousMediaIndex(nowIndex)];

        private int GetMediaIndex() =>
            isShuffled ? shuffler.FirstOrDefault(x => x.Item.Music == playingMusic).Index - 1 : srcPlaying.FirstOrDefault(x => x.Item.Music == playingMusic).Index - 1;

        private int GetNextMediaIndex(int? media = null)
        {
            int index = media ?? GetMediaIndex();
            if (index == srcPlaying.Count - 1)
                return -1;
            return isShuffled ? shuffler.ElementAt(index + 1).Index - 1 : index + 1;
        }

        private int GetPreviousMediaIndex(int? media = null)
        {
            int index = media ?? GetMediaIndex();
            if (index == 0)
                return -1;
            return isShuffled ? shuffler.ElementAt(index - 1).Index - 1 : index - 1;
        }

        protected void Shuffle(bool state)
        {
            if (state)
                shuffler.Shuffle();
            isShuffled = state;
        }
    }
}
