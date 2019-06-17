﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net.Http;
using static SyncSaberLib.Utilities;
using SyncSaberLib.Data;
using SyncSaberLib.Web;
using System.Reflection;

namespace SyncSaberLib
{
    public class SyncSaber
    {
        public static SyncSaber Instance;
        public string CustomSongsPath;
        private static readonly string zipExtension = ".zip";

        public static string VersionCheck()
        {
            string retStr = "";
            var getPage = WebUtils.httpClient.GetAsync("https://raw.githubusercontent.com/Zingabopp/SyncSaberService/master/Status");
            getPage.Wait();
            
            if(getPage.Result.StatusCode != HttpStatusCode.OK)
            {
                retStr = "Unable to check version.";
                Logger.Warning(retStr);
                return retStr;
            }
            var statusText = getPage.Result.Content.ReadAsStringAsync().Result.Split(
                Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(',')).ToDictionary(s => s[0], x => x[1]);
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = string.Join(".", version.Major, version.Minor, version.Build);
            if (statusText.ContainsKey(versionStr))
            {
                var status = statusText[versionStr];
                switch (status)
                {
                    case "Broken":
                        string errorMsg = "This version of SyncSaberService is no longer functional.";
                        if (statusText.Values.Any(s => s != "Broken"))
                            errorMsg = errorMsg + " Please update to a newer version.";
                        throw new OutOfDateException(errorMsg);
                    case "Outdated":
                        retStr = "This version of SyncSaberService is outdated, please update to a newer version.";
                        Logger.Warning(retStr);
                        break;
                    case "Latest":
                        retStr = "Running the latest version of SyncSaberService.";
                        Logger.Info(retStr);
                        break;
                    default:
                        retStr = "Running an unknown version of SyncSaberService.";
                        Logger.Info(retStr);
                        break;
                }
            }
            return retStr;
        }

        public SyncSaber()
        {
            Instance = this;
            VersionCheck();
            
            _historyPath = Path.Combine(Config.BeatSaberPath, "UserData", "SyncSaberHistory.txt");
            if (File.Exists(_historyPath + ".bak"))
            {
                if (File.Exists(_historyPath))
                {
                    File.Delete(_historyPath);
                }
                File.Move(_historyPath + ".bak", _historyPath);
            }
            if (File.Exists(_historyPath))
            {
                _songDownloadHistory = File.ReadAllLines(_historyPath).ToList<string>();
            }
            else
                File.Create(_historyPath); // TODO: Check if this works
            if (Directory.Exists(Config.BeatSaberPath))
            {
                CustomSongsPath = Path.Combine(Config.BeatSaberPath, @"Beat Saber_Data\CustomLevels");
                if (!Directory.Exists(CustomSongsPath))
                {
                    Directory.CreateDirectory(CustomSongsPath);
                }
            }

            FeedReaders = new Dictionary<string, IFeedReader> {
                {BeatSaverReader.NameKey, new BeatSaverReader() },
                {BeastSaberReader.NameKey, new BeastSaberReader(Config.BeastSaberUsername, Config.MaxConcurrentPageChecks) },
                {ScoreSaberReader.NameKey, new ScoreSaberReader() }
            };
        }

        private void UpdatePlaylist(Playlist playlist, string songHash, string songIndex, string songName)
        {
            if (playlist == null)
            {
                Logger.Warning($"playlist is null in UpdatePlaylist for song {songIndex} - {songName}");
                return;
            }
            bool songAlreadyInPlaylist = playlist.Songs.Exists(s => s.hash.ToUpper() == songHash.ToUpper());
            
            if (!songAlreadyInPlaylist)
            {
                playlist.TryAdd(songHash, songIndex, songName);
                Logger.Info($"Success adding new song \"{songName}\" with BeatSaver index {songIndex} to playlist {playlist.Title}!");
            }
        }

        [Obsolete("Does not work anymore, returns immediately")]
        private void RemoveOldVersions(string songIndex)
        {
            return;
            if (!Config.DeleteOldVersions)
            {
                return;
            }
            string[] customSongDirectories = Directory.GetDirectories(CustomSongsPath);
            string id = songIndex.Substring(0, songIndex.IndexOf("-"));
            string version = songIndex.Substring(songIndex.IndexOf("-") + 1);
            foreach (string directory in customSongDirectories)
            {
                try
                {
                    string directoryName = Path.GetFileName(directory);
                    if (_beatSaverRegex.IsMatch(directoryName) && directoryName != songIndex)
                    {
                        string directoryId = directoryName.Substring(0, directoryName.IndexOf("-"));
                        if (directoryId == id)
                        {

                            string directoryVersion = directoryName.Substring(directoryName.IndexOf("-") + 1);
                            string directoryToRemove = directory;
                            string currentVersion = songIndex;
                            string oldVersion = directoryName;
                            if (Convert.ToInt32(directoryVersion) > Convert.ToInt32(version))
                            {
                                directoryToRemove = Path.Combine(CustomSongsPath, songIndex);
                                currentVersion = directoryName;
                                oldVersion = songIndex;
                            }
                            Logger.Info($"Deleting old song with identifier \"{oldVersion}\" (current version: {currentVersion})");
                            Directory.Delete(directoryToRemove, true);
                        }
                    }
                    else if (_digitRegex.IsMatch(directoryName) && directoryName == id)
                    {
                        Logger.Info($"Deleting old song with identifier \"{directoryName}\" (current version: {id}-{version})");
                        Directory.Delete(directory, true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception("Exception when trying to remove old versions: ", ex);
                }
            }
        }

        public void ClearResultQueues()
        {
            SuccessfulDownloads.Clear();
            FailedDownloads.Clear();
        }

        public void OnJobFinished(DownloadJob job)
        {
            if (job.Result == DownloadJob.JobResult.SUCCESS)
                SuccessfulDownloads.Enqueue(job);
            else
                FailedDownloads.Enqueue(job);
            // TODO: Add fail reason to FailedDownloads items
        }

        public void ScrapeNewSongs()
        {
            var lastBSScrape = DateTime.Now - ScrapedDataProvider.BeatSaverSongs.Data.Max(s => s.ScrapedAt);
            var lastSSScrape = DateTime.Now - ScrapedDataProvider.ScoreSaberSongs.Data.Max(s => s.ScrapedAt);
            Logger.Info($"Scraping new songs. Last Beat Saver scrape was {lastBSScrape.ToString()} ago. Last ScoreSaber scrape was {lastSSScrape.ToString()} ago.");
            var bsReader = FeedReaders[BeatSaverReader.NameKey] as BeatSaverReader;
            BeatSaverReader.ScrapeBeatSaver(200, true);
            ScrapedDataProvider.BeatSaverSongs.WriteFile();
            
            if (lastSSScrape.TotalHours > 3)
                ScoreSaberReader.ScrapeScoreSaber(1000, 500, true, 2);
            ScrapedDataProvider.ScoreSaberSongs.WriteFile();
        }

        public void DownloadSongsFromFeed(string feedType, IFeedSettings _settings)
        {
            if (!FeedReaders.ContainsKey(feedType))
            {
                Logger.Error($"Invalid feed type passed to DownloadSongsFromFeed: {feedType}");
                return;
            }
            var reader = FeedReaders[feedType];
            DateTime startTime = DateTime.Now;
            Dictionary<int, SongInfo> songs;
            List<Playlist> playlists = new List<Playlist>();
            playlists.Add(_syncSaberSongs);
            playlists.AddRange(reader.PlaylistsForFeed(_settings.FeedIndex));
            try
            {
                songs = reader.GetSongsFromFeed(_settings);
            }
            catch (InvalidCastException)
            {
                Logger.Error($"Wrong type of IFeedSettings given to {feedType}");
                return;
            }
            int totalSongs = songs.Count;
            Logger.Debug($"Finished checking pages, found {totalSongs} songs");
            List<SongInfo> matchedSongs = DownloadSongs(songs, out (List<SongInfo> exists, List<SongInfo> history) skippedSongs, _settings.UseSongKeyAsOutputFolder);
            Logger.Debug("Jobs finished, Processing downloads...");
            int downloadCount = SuccessfulDownloads.Count;
            int failedCount = FailedDownloads.Count;
            ProcessDownloads(playlists);
            foreach (var p in playlists)
            {
                matchedSongs.ForEach(s => p.TryAdd(s.hash, s.key, s.songName));
                p.WritePlaylist();
            }
            var timeElapsed = (DateTime.Now - startTime);
            Logger.Info($"Downloaded {downloadCount} songs from {reader.Source}'s {_settings.FeedName} feed in {FormatTimeSpan(timeElapsed)}. " +
                $"Skipped {skippedSongs.exists.Count} songs that already exist and {skippedSongs.history.Count} that are in history{(failedCount > 0 ? $", failed to download {failedCount} songs." : "")}.");

        }

        private void ProcessDownloads(List<Playlist> playlists = null)
        {
            if (playlists == null)
                playlists = new List<Playlist>();
            playlists.Add(_syncSaberSongs);
            Logger.Debug("Processing downloads...");
            DownloadJob job;
            while (SuccessfulDownloads.TryDequeue(out job))
            {
                if (!_songDownloadHistory.Contains(job.Song.key))
                    _songDownloadHistory.Add(job.Song.key);

                //UpdatePlaylist(_syncSaberSongs, job.Song.key, job.Song.name);
                //foreach (var playlist in playlists)
                //{
                //    // TODO: fix this maybe
                //    UpdatePlaylist(playlist, job.Song.hash, job.Song.key, job.Song.songName);

                //}
                RemoveOldVersions(job.Song.key);
            }
            while (FailedDownloads.TryDequeue(out job))
            {
                if (!_songDownloadHistory.Contains(job.Song.key) && job.Result != DownloadJob.JobResult.TIMEOUT)
                {
                    _songDownloadHistory.Add(job.Song.key);
                }
                Logger.Error($"Failed to download {job.Song.key} by {job.Song.authorName}");
            }

            Utilities.WriteStringListSafe(_historyPath, _songDownloadHistory.Distinct().ToList(), true);
            //foreach (Playlist playlist in playlists)
                //playlist.WritePlaylist();
        }
        
        /// <summary>
        /// Downloads the songs in the provided Dictionary.
        /// </summary>
        /// <param name="queuedSongs"></param>
        /// <param name="skippedsongs"></param>
        /// <param name="useSongKeyAsOutputFolder"></param>
        /// <returns></returns>
        public List<SongInfo> DownloadSongs(Dictionary<int, SongInfo> queuedSongs, out (List<SongInfo> exists, List<SongInfo> history) skipped, bool useSongKeyAsOutputFolder)
        {
            //var existingSongs = Directory.GetDirectories(CustomSongsPath);
            string tempPath = "";
            string outputPath = "";
            List<SongInfo> matchedSongs = new List<SongInfo>();
            
            skipped.exists = new List<SongInfo>();
            skipped.history = new List<SongInfo>();
            DownloadBatch jobs = new DownloadBatch();
            jobs.JobCompleted += OnJobFinished;
            foreach (var song in queuedSongs.Values)
            {
                tempPath = Path.Combine(Path.GetTempPath(), song.key + zipExtension);
                if (useSongKeyAsOutputFolder)
                    outputPath = Path.Combine(CustomSongsPath, $"{song.key} ({MakeSafeFilename(song.songName)} - {MakeSafeFilename(song.authorName)})");
                else
                    outputPath = CustomSongsPath;
                bool songExists = Directory.Exists(outputPath); // TODO: Check if it exists in the SongHash file.
                bool songInHistory = _songDownloadHistory.Contains(song.key);
                if((songExists && songInHistory) || !songInHistory)
                {
                    matchedSongs.Add(song);
                }
                if (songExists || songInHistory)
                {
                    if (songExists)
                        skipped.exists.Add(song);
                    else
                        skipped.history.Add(song);
                    //Logger.Debug($"Skipping song - SongExists: {songExists}, SongInHistory: {songInHistory}");
                    continue; // We already have the song or don't want it, skip
                }
                DownloadJob job = new DownloadJob(song, tempPath, outputPath);
                jobs.AddJob(job);
            }
            jobs.RunJobs().Wait();
            return matchedSongs;
        }

        private static readonly Regex _digitRegex = new Regex("^[0-9]+$", RegexOptions.Compiled);

        private static readonly Regex _beatSaverRegex = new Regex("^[0-9]+-[0-9]+$", RegexOptions.Compiled);

        private readonly string _historyPath;

        private ConcurrentQueue<DownloadJob> _successfulDownloads;
        private ConcurrentQueue<DownloadJob> SuccessfulDownloads
        {
            get
            {
                if (_successfulDownloads == null)
                    _successfulDownloads = new ConcurrentQueue<DownloadJob>();
                return _successfulDownloads;
            }
        }

        private ConcurrentQueue<DownloadJob> _failedDownloads;
        private ConcurrentQueue<DownloadJob> FailedDownloads
        {
            get
            {
                if (_failedDownloads == null)
                    _failedDownloads = new ConcurrentQueue<DownloadJob>();
                return _failedDownloads;
            }
        }

        private readonly List<Task<bool>> _runningJobs = new List<Task<bool>>();

        private List<string> _songDownloadHistory = new List<string>();

        private readonly Playlist _syncSaberSongs = new Playlist("SyncSaberPlaylist", "SyncSaber Playlist", "SyncSaber", "1");

        public Dictionary<string, IFeedReader> FeedReaders;

    }

    public class OutOfDateException : ApplicationException
    {
        public OutOfDateException(string message) : base(message)
        {
        }
    }

}