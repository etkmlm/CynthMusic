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
        private TimeSpan? ps;

        public PlayerManager(ref MediaElement player)
        {
            this.player = player;
            ResetShuffler();
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            timer.Tick += (a, b) =>
            {
                pos = this.player.Position;
                PositionChanged.Invoke(this.player.Position, this.player.NaturalDuration, this.player.BufferingProgress);
            };

            player.MediaFailed += (a, b) =>
            MediaFail.Invoke(b.ErrorException);

            player.MediaOpened += (a, b) =>
            {
                if (ps.HasValue)
                {
                    while (!this.player.NaturalDuration.HasTimeSpan) ;
                    this.player.Position = ps.Value;
                    ps = null;
                }

                retry = 5;
            };

            player.MediaEnded += (a, b) => MediaEnd.Invoke(GetMediaIndex());

        }

        protected void Play(IMusic music, TimeSpan? position = null, bool autoPlay = true)
        {
            playingMusic = music;
            player.Stop();
            player.Source = null;
            player.Source = new Uri(music.PlayURL);

            if (autoPlay)
                player.Play();
            if (!timer.IsEnabled)
                timer.Start();

            MediaChanged.Invoke(music, autoPlay);
            isPlaying = autoPlay;
            isLoaded = true;

            if (position.HasValue)
                ps = position.Value;
        }

        private TimeSpan pos;

        public void Resume()
        {
            player.Play();
            player.Position = pos;
            isPlaying = true;
        }

        protected void Pause()
        {
            player.Pause();
            pos = player.Position;
            isPlaying = false;
        }

        protected void Stop()
        {
            player.Stop();
            player.Source = null;
            playingMusic = null;
            isPlaying = false;
            isShuffled = false;
            isLoaded = false;
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
            isShuffled ? shuffler.FindIndex(x => x.Item.Music == playingMusic) : srcPlaying.FirstOrDefault(x => x.Item.Music == playingMusic).Index - 1;

        private int GetNextMediaIndex(int? media = null)
        {
            int index = media ?? GetMediaIndex();
            return index == srcPlaying.Count - 1 ? index : (isShuffled ? shuffler.ElementAt(index + 1).Index - 1 : index + 1);
        }

        private int GetPreviousMediaIndex(int? media = null)
        {
            int index = media ?? GetMediaIndex();
            return index == 0 ? 0 : (isShuffled ? shuffler.ElementAt(index - 1).Index - 1 : index - 1);
        }

        protected void Shuffle(bool state)
        {
            if (state)
                shuffler.Shuffle();
            isShuffled = state;
        }
    }
}
