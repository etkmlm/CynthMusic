/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore;
using CynthCore.Entities;
using CynthMusic.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;

namespace CynthMusic
{
    public class PlayerService : PlayerManager
    {
        private ListView lvPlaying;
        private MusicService musicService;
        private PlaylistManager playlistManager => MainWindow.playlistManager;
        private MainWindow window => (MainWindow)App.Current.MainWindow;
        private ConfigService config => MainWindow.configService;
        private AddonManager addon => MainWindow.addonManager;

        private CancellationTokenSource cancellation;

        public OrderableCollection<ColorableMusic> srcPlaying;
        public static int PlayingListID = -1;

        private bool isDeletingOpen = true;
        private List<int> toDeleteList;
        private string playingListYT;

        public PlayerService(ref ListView lvPlaying, ref MediaElement player, MusicService musicService, Action<string> setIconTip) :
            base(ref player, setIconTip)
        {
            this.musicService = musicService;
            this.lvPlaying = lvPlaying;

            cancellation = new CancellationTokenSource();
            srcPlaying = new OrderableCollection<ColorableMusic>();
            toDeleteList = new List<int>();
            lvPlaying.ItemsSource = srcPlaying;
        }
        public void CancelToken()
        {
            cancellation.Cancel();
            cancellation = new CancellationTokenSource();
        }
        public CancellationToken GetToken() =>
            cancellation.Token;
        public async Task<IMusicList> GetLoadedList() =>
            await playlistManager.GetAsync(PlaylistManager.loadedPlaylistId);

        #region Play
        private async Task PlayNormalMusicList(MusicList list, bool play)
        {
            if (!list.Verify())
            {
                await App.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var musics = list.Musics.Where(x => x != null);
                    list.Musics = musics;
                    await playlistManager.UpdateMusicsAsync(list.ID, list.Musics);
                    await PlayNormalMusicList(list, play);
                });
                return;
            }
            foreach (var m in list.Musics)
            {
                m.IsFavourite = await musicService.IsFavourite(m.SaveIdentity);
                srcPlaying.Add(new ColorableMusic(m));
            }
            if (!string.IsNullOrEmpty(list.Musics.ElementAt(0).PlayURL) && play)
                PlayList();

            await Task.Run(async () =>
            {
                isDeletingOpen = false;
                await LoadYouTubeMusics(play);
                isDeletingOpen = true;
            }, GetToken());

            await lvPlaying.Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < toDeleteList.Count; i++)
                    RemoveMusic(toDeleteList[i]);
            });
        }
        private async Task PlayYouTubeMusicList(YouTubeMusicList list, CancellationToken token, bool play) =>
            await Task.Run(async () =>
            {
                playingListYT = list.YouTubeID;
                lvPlaying.Dispatcher.Invoke(() =>
                {
                    foreach (var x in list.Musics)
                        srcPlaying.Add(new ColorableMusic(x));
                });
                await foreach (var x in list.MusicsAsync)
                {
                    if (token.IsCancellationRequested)
                        break;
                    bool isFavourite = await musicService.IsFavourite(x.SaveIdentity);
                    await lvPlaying.Dispatcher.InvokeAsync(() =>
                    {
                        var item = srcPlaying[x.ID];
                        item.Item.Music.PlayURL = x.PlayURL;
                        item.Item.Music.Length = x.Length;
                        srcPlaying.SetItem(x.ID, item);
                        lvPlaying.Items.Refresh();
                        if (x.ID == 1 && play)
                            PlayFirst();
                    });
                    await Task.Delay(100);
                }
            });
        public async Task PlayMusicList(IMusicList list, bool play = true, IEnumerable<int> shuffle = null)
        {
            srcPlaying.Clear();
            Stop();
            if (shuffle != null)
            {
                Shuffle(true, shuffle);
                window.btnShuffle.Foreground = new LinearGradientBrush(Colors.Red, Colors.Aqua, 45);
            }

            PlayingListID = list.ID == -1 ? -2 : list.ID;
            SaveState(false);
            if (list is YouTubeMusicList l)
                await PlayYouTubeMusicList(l, GetToken(), play);
            else
                await PlayNormalMusicList((MusicList)list, play);

        }
        public async Task PlayMusicWithLoad(Orderable<ColorableMusic> music, TimeSpan? position = null, bool start = true) =>
            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (music.Item.Music is not YouTubeMusic)
                {
                    Play(music.Item.Music.SaveIdentity, position);
                    return;
                }
                var m = await musicService.GetConvertedYouTubeMusicAsync(((YouTubeMusic)music.Item.Music).YouTubeUri);
                var item = srcPlaying[music.Index - 1];
                item.Item.Music.PlayURL = m.Value.PlayURL;
                item.Item.Music.Length = m.Value.Length;
                srcPlaying.SetItem(music.Index - 1, item);
                lvPlaying.Items.Refresh();
                Play(music.Item.Music.SaveIdentity, position);
            });
        public async Task PlayLocation(Button btn)
        {
            var item = (Orderable<Location>)btn.DataContext;
            var list = playlistManager.GetFromPath(item.Item.Path, false).ElementAt(0);
            if (list.Musics.Count() == 0)
                return;
            await PlayMusicList(list);
        }
        public async Task PlayLoadedList() =>
            await PlayMusicList(await GetLoadedList());
        public async Task PlayFavourites()
        {
            Stop();
            srcPlaying.Clear();
            foreach (var x in addon.GetAllFavourites())
                srcPlaying.Add(new ColorableMusic(x));
            if (srcPlaying[0].Item.Music.PlayURL != null)
                PlayList();
            await Task.Run(async () => await LoadYouTubeMusics(), GetToken());
        }
        #endregion

        #region Utils
        protected async Task LoadYouTubeMusics(bool play = true)
        {
            foreach (var newItem in srcPlaying.Where(x => x.Item.Music.PlayURL == null).ToList())
            {
                if (cancellation.IsCancellationRequested || srcPlaying.Count == 0)
                    return;
                string a = ((YouTubeMusic)newItem.Item.Music).YouTubeUri;
                var get = await musicService.GetConvertedYouTubeMusicAsync(a);
                newItem.Item.Music.Author = playlistManager.GetAlgCheck() ? MusicService.AuthorAlgorithm(get.Value.Name, playlistManager.GetAlgAuthors(), get.Value.Author) : get.Value.Author;
                newItem.Item.Music.Length = get.Value.Length;
                newItem.Item.Music.PlayURL = get.Value.PlayURL;
                if (App.Current == null)
                    return;
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (newItem.Index > srcPlaying.Count)
                        return;
                    srcPlaying.SetItem(newItem.Index - 1, newItem);
                    if (newItem.Index == 1 && play)
                        PlayList();
                    lvPlaying.Items.Refresh();
                });
            }
        }
        public async Task RefreshPlaying() => await lvPlaying.Dispatcher.InvokeAsync(() => lvPlaying.Items.Refresh());
        public void RemoveMusic(int index)
        {
            if (isDeletingOpen)
            {
                srcPlaying.DeleteItem(index);
                if (GetPlayingID() == index + 1)
                    ConvertPlay(srcPlaying[index]);
            }
            else
                toDeleteList.Add(index);
        }
        public void SaveState(bool first = true)
        {
            if (!IsPlayingList)
            {
                config.Set("LASTID", null);
                return;
            }
            string listId = PlayingListID == -2 ? "---" + playingListYT : PlayingListID.ToString();
            string shuffle = isShuffled ? string.Join(',', playingListOrders) : null;
            int playing = GetPlayingID() ?? 0;
            string pos = GetPosition();

            config.Set("LASTID", $"{listId}||{shuffle}||{playing}||{pos}");
            if (!first)
                return;
            string x = ((int)window.volume) + "," + (int)window.sldVolume.Value;
            config.Set("VOL", x);
        }
        public async Task RestoreState() =>
            await Task.Run(async () =>
            {
                try
                {
                    string[] state = config.Get("LASTID").Split("||");
                    if (state.Length != 4)
                        return;

                    IMusicList list;
                    if (state[0].StartsWith("---"))
                    {
                        list = await playlistManager.GetYouTubeListAsync(YouTubeMusicList.GetLocation(state[0][3..]), playlistManager.GetAlgCheck(), playlistManager.GetAlgAuthors());
                        if (list == null)
                            return;
                    }
                    else
                        list = await playlistManager.GetAsync(int.Parse(state[0]), playlistManager.GetAlgCheck(), playlistManager.GetAlgAuthors());

                    if (list == null)
                        return;
                    await lvPlaying.Dispatcher.InvokeAsync(async () => 
                        await PlayMusicList(list, false, !string.IsNullOrWhiteSpace(state[1]) ? state[1].Split(',').Select(x => int.Parse(x)) : null));
                    int count = list.Musics.Count();
                    while (srcPlaying.Count != count) ;

                    try
                    {
                        int number = int.Parse(state[2]);
                        await PlayMusicWithLoad(srcPlaying[number <= 1 ? 0 : number - 1], TimeSpan.ParseExact(state[3], @"hh\.mm\.ss", CultureInfo.CurrentCulture), false);
                    }
                    catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidOperationException)
                    {

                    }
                    finally
                    {
                        while (!isPlaying) ;
                    }

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        string[] vol = config.Get("VOL").Split(',');
                        window.SetVolume(int.Parse(vol[1]), int.Parse(vol[0]));

                        if (config.Get("PLINTRAY").ToLower() == "true")
                            window.SwitchVisibility();
                        else
                        {
                            window.SetPlayState(false);
                            TogglePlay(false);
                        }
                    });
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.StackTrace + "\n\n" + e.Message);
                }
            });
        #endregion
    }
}
