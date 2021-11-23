/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore;
using CynthCore.Entities;
using CynthMusic.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace CynthMusic.Management
{
    public class PlaylistManager : PlaylistService
    {
        private ListView lvPlaylists;
        private ListView lvPlaying;
        private PlayerService player => MainWindow.playerService;

        private readonly OrderableCollection<IMusicList> srcPlaylists;
        private readonly OrderableCollection<IMusic> srcMusics;

        public static int loadedPlaylistId = -1;

        public PlaylistManager(ref ListView lvPlaylists, ref ListView lvPlaylist, ref ListView lvPlaying, DataService data, MusicService mS, YouTubeClient client) :
            base(data, mS, client)
        {
            this.lvPlaylists = lvPlaylists;
            this.lvPlaying = lvPlaying;
            srcPlaylists = new OrderableCollection<IMusicList>();
            srcMusics = new OrderableCollection<IMusic>();
            
            lvPlaylists.ItemsSource = srcPlaylists;
            lvPlaylist.ItemsSource = srcMusics;
        }

        #region Basics
        public IMusicList GetMusicList(int index) =>
            srcPlaylists[index].Item;
        public async Task LoadPlaylists() =>
            await lvPlaylists.Dispatcher.InvokeAsync(async () =>
            {
                srcPlaylists.Clear();
                srcMusics.Clear();
                await foreach (var x in GetAllAsync(GetAlgCheck(), GetAlgAuthors()))
                    srcPlaylists.Add(x);
            });
        public async Task DeletePlaylistAsync(Button btn)
        {
            var item = srcPlaylists.ObjectConvert(btn.DataContext);
            if (!item.HasValue)
                return;
            await DeleteAsync(item.Value.Item.ID);
            srcPlaylists.DeleteItem(x => x.Item.ID == item.Value.Item.ID);
        }
        public async Task<bool> RenamePlaylistAsync(int id, string newName)
        {
            if (await Contains(newName) || string.IsNullOrWhiteSpace(newName))
                return false;
            await Rename(id, newName);
            var item = srcPlaylists.FirstOrDefault(x => x.Item.ID == id);
            item.Item.Name = newName;
            srcPlaylists.SetItem(item.Index - 1, item);
            await lvPlaylists.Dispatcher.InvokeAsync(() => lvPlaylists.Items.Refresh());
            return true;
        }
        public async Task<int> AddPlaylistAsync(IMusicList list)
        {
            int id = await AddAsync(list);
            list.ID = id;
            srcPlaylists.Add(list);
            return list.ID;
        }
        public async Task<bool> LoadPlaylist(Button btn = null)
        {
            var item = srcPlaylists.ObjectConvert(btn.DataContext);
            if (!item.HasValue || item.Value.Item is not MusicList)
                return false;
            srcMusics.Clear();
            loadedPlaylistId = item.Value.Item.ID;
            IMusicList list = await GetAsync(loadedPlaylistId, GetAlgCheck(), GetAlgAuthors());
            if (list.Musics != null)
                foreach (var x in list.Musics)
                    srcMusics.Add(x);
            return true;
        }
        #endregion

        #region Musics
        public async Task DeleteMusicAsync(Button btn)
        {
            var item = srcMusics.ObjectConvert(btn.DataContext);
            srcMusics.DeleteItem(item.Value);
            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                await UpdateMusicsAsync(loadedPlaylistId, srcMusics.Select(x => x.Item));

                if (PlayerService.PlayingListID == loadedPlaylistId)
                    player.RemoveMusic(item.Value.Index - 1);
            });
        }
        public async Task AddMusicsAsync(IMusicList list, IEnumerable<IMusic> musics) =>
            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                await UpdateMusicsAsync(list.ID, list.Musics.Concat(musics));
                bool a = loadedPlaylistId == PlayerService.PlayingListID;
                foreach (var x in musics)
                {
                    srcMusics.Add(x);
                    if (a)
                    {
                        player.srcPlaying.Add(new ColorableMusic(x));
                        if (x is YouTubeMusic)
                        {
                            int index = player.srcPlaying.LastOrDefault().Index;
                            var music = await musicService.GetConvertedYouTubeMusicAsync(((YouTubeMusic)x).YouTubeUri);
                            if (music == null)
                            {
                                string msg = ExceptionManager.SolveHttp(ExceptionManager.GetExceptions("getMusicWithStream").LastOrDefault());
                                new AlertBox("Hata", msg).ShowDialog();
                                return;
                            }
                            player.srcPlaying[index - 1].Item.Music.PlayURL = music.Value.PlayURL;
                            player.srcPlaying[index - 1].Item.Music.Length = music.Value.Length;
                            lvPlaying.Items.Refresh();
                        }
                    }
                }
            });
        public async Task UpdateMusicsAsync(int id, IEnumerable<IMusic> musics)
        {
            string strMsc = ConvertMusicListToString(musics);
            await UpdateMusics(id, strMsc);
            var x = srcPlaylists.FirstOrDefault(x => x.Item.ID == id);
            x.Item = await GetAsync(x.Item.ID);
            srcPlaylists.SetItem(x.Index - 1, x);
            lvPlaylists.Items.Refresh();
        }
        #endregion

        #region Utils
        public IEnumerable<Orderable<IMusic>> Filter(string q) =>
            string.IsNullOrWhiteSpace(q) ? srcMusics : 
            srcMusics.Where(x => 
                x.Item.Name.ToLower().Contains(q) || 
                (x.Item.Author != null && x.Item.Author.ToLower().Contains(q)));
        public async Task ExchangeOrders(int from, int to)
        {
            srcMusics.MoveItem(from, to);
            string musics = ConvertMusicListToString(srcMusics.Select(x => x.Item));
            await UpdateMusics(loadedPlaylistId, musics);

            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                if (PlayerService.PlayingListID == loadedPlaylistId)
                    player.srcPlaying.MoveItem(from, to);
                player.ApplyExchange();
            });
        }
        public async Task ConvertYouTubeListAsync(YouTubeMusicList list) => await Task.Run(async () =>
        {
            await ConvertYouTubeToNormalAsync(list.ID, list.Location, list.Name);
            await LoadPlaylists();
        });
        public void ExportList(IMusicList list)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Dışa aktarılacak konum seçin",
                Filter = "Corelium Müzik Listesi|*.cml"
            };
            bool? show = dialog.ShowDialog();
            if (!show.HasValue || !show.Value)
                return;
            string destination = dialog.FileName;
            ExportMusicList(list, destination);
        }
        public async Task<bool> ImportList()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Bir dosya seç",
                Filter = "Cynth Müzik Listesi|*.cml"
            };
            bool? show = dialog.ShowDialog();
            if (!show.HasValue || !show.Value)
                return true;
            string file = dialog.FileName;
            var list = await ImportMusicList(file);
            int num = 0;
            string name = list.Name;
            while (await Contains(name))
                name = list.Name + ++num;
            list.Name = name;
            await AddAsync(list);
            return list != null;
        }
        #endregion

        public bool GetAlgCheck() =>
            MainWindow.configService.Get("FAUTH").ToLower() == "true";
        public string[] GetAlgAuthors()
        {
            return null;
        }
    }
}
