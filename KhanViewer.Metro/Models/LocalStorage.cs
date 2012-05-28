﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace KhanViewer.Models
{
    public static class LocalStorage
    {
        static readonly string PlaylistFileName = "playlists.xml";
        static readonly string VideosFileName = "videos.xml";
        static readonly string LAST_VIDEO_FILENAME = "lastvideoviewed.xml";

        public static void GetPlaylists(Action<IEnumerable<PlaylistItem>> result)
        {
            GetFile(PlaylistFileName).ContinueWith(value =>
            {
                var file = value.Result;

                if (file == null)
                {
                    result(new PlaylistItem[] { new PlaylistItem { Name = "Loading", Description = "From Server ..." } });
                    return;
                }

                var folder = ApplicationData.Current.LocalFolder;

                folder.OpenStreamForReadAsync(PlaylistFileName).ContinueWith(filevalue =>
                    {
                        using (var stream = filevalue.Result)
                        {
                            DataContractSerializer serializer = new DataContractSerializer(typeof(PlaylistItem[]));
                            var localCats = serializer.ReadObject(stream) as PlaylistItem[];

                            if (localCats == null || localCats.Length == 0)
                            {
                                result(new PlaylistItem[] { new PlaylistItem { Name = "Loading from server ...", Description = "local cache was empty" } });
                                return;
                            }

                            // heh, almost read this variable as lolCats 
                            result(localCats);
                        }
                    });
            });

        }

        public static void GetVideos(string playlistName, Action<IEnumerable<VideoItem>> result)
        {
            string filename = playlistName + VideosFileName;
            filename = IsValidFilename(filename);

            FileExists(filename).ContinueWith(exists =>
                {
                    if (!exists.Result)
                    {
                        result(new VideoItem[] { new VideoItem { Name = "Loading", Description = "From Server ..." } });
                        return;
                    }

                    var folder = ApplicationData.Current.LocalFolder;

                    folder.OpenStreamForReadAsync(filename).ContinueWith(readtask =>
                        {
                            using (var stream = readtask.Result)
                            {
                                VideoItem[] localVids;

                                try
                                {
                                    DataContractSerializer serializer = new DataContractSerializer(typeof(VideoItem[]));
                                    localVids = serializer.ReadObject(stream) as VideoItem[];
                                }
                                catch
                                {
                                    result(GetPlaceHolder());
                                    return;
                                }

                                if (localVids == null || localVids.Length == 0)
                                {
                                    result(GetPlaceHolder());
                                    return;
                                }

                                result(localVids);
                            }
                        });
                });
        }
        
        /// <summary>Gets you the last viewed video. Or null if none viewed previously.</summary>
        public static async Task<VideoItem> GetLastViewedAsync()
        {
            var folder = ApplicationData.Current.LocalFolder;
            if (await FileExists(LAST_VIDEO_FILENAME))
            {
                return null;
            }

            var readtask = await folder.OpenStreamForReadAsync(LAST_VIDEO_FILENAME);
                        
            using (var stream = readtask)
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(VideoItem));
                var deserializedVid = serializer.ReadObject(stream) as VideoItem;
                return deserializedVid;
            }
        }

        /// <summary>If a specific video item is available on disk, it will be deserialized.
        /// Otherwise will return null.</summary>
        public static void GetVideo(string playlistName, string videoName, Action<VideoItem> result)
        {
            string listpath = IsValidFilename(playlistName);
            string vidpath = IsValidFilename(videoName);
            string filename = Path.Combine(listpath, vidpath) + ".xml";

            var folder = ApplicationData.Current.LocalFolder;
            FileExists(filename).ContinueWith(exists =>
                {
                    if (!exists.Result)
                    {
                        result(null);
                        return;
                    }

                    folder.OpenStreamForReadAsync(filename).ContinueWith(readtask =>
                        {
                            using (var stream = readtask.Result)
                            {
                                DataContractSerializer serializer = new DataContractSerializer(typeof(VideoItem));
                                var deserializedVid = serializer.ReadObject(stream) as VideoItem;
                                result(deserializedVid);
                            }
                        });
                });
        }

        public static void SaveVideo(VideoItem item)
        {
            string catpath = IsValidFilename(item.Parent);
            string vidpath = IsValidFilename(item.Name);
            string filename = Path.Combine(catpath, vidpath) + ".xml";
            
            var folder = CreateDirectory(catpath);

            WriteFile(filename).ContinueWith(opentask =>
                {
                    using (var stream = opentask.Result)
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(VideoItem));
                        serializer.WriteObject(stream, item);
                    }
                });
                       
            WriteFile(LAST_VIDEO_FILENAME).ContinueWith(opentask =>
                {
                    using (var stream = opentask.Result)
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(VideoItem));
                        serializer.WriteObject(stream, item);
                    }
                });
        }

        public static void SavePlaylists<T>(T[] playlists)
        {
            WriteFile(PlaylistFileName).ContinueWith(opentask =>
                {
                    using (var stream = opentask.Result)
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(T[]));
                        serializer.WriteObject(stream, playlists);
                    }
                });
        }

        public static void SaveVideos<T>(string playlistName, T[] videos)
        {
            string filename = playlistName + VideosFileName;

            filename = IsValidFilename(filename);

            WriteFile(filename).ContinueWith(opentask =>
                {
                    using (var stream = opentask.Result)
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(T[]));
                        serializer.WriteObject(stream, videos);
                    }
                });
        }

        /// <summary>Verifies if there are invalid characters, and if so, removes them from the filename</summary>
        /// <remarks>Method derived from information here:
        /// http://stackoverflow.com/questions/333175/is-there-a-way-of-making-strings-file-path-safe-in-c</remarks>
        private static string IsValidFilename(string testName)
        {
            var invalid = System.IO.Path.GetInvalidPathChars().Union(new char[] { ':', ' ' }).ToArray();
            Regex containsABadCharacter = new Regex("[" + Regex.Escape(new string(invalid)) + "]");
            if (containsABadCharacter.IsMatch(testName)) {
                foreach(var c in invalid)
                {
                    testName = testName.Replace(c.ToString(), String.Empty);
                }
            };

            return testName;
        }

        private static VideoItem[] GetPlaceHolder()
        {
            return new VideoItem[] { new VideoItem { Name = "Loading from server ...", Description = "local cache was empty." } };
        }

        private async static Task<StorageFile> GetFile(string path)
        {
            var folder = ApplicationData.Current.LocalFolder;
            try
            {
                return await folder.GetFileAsync(PlaylistFileName);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private async static Task<bool> FileExists(string path)
        {
            var folder = ApplicationData.Current.LocalFolder;
            try
            {
                var file = await folder.GetFileAsync(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private async static Task<Stream> WriteFile(string path)
        {
            var folder = ApplicationData.Current.LocalFolder;
            return await folder.OpenStreamForWriteAsync(path, CreationCollisionOption.ReplaceExisting);
        }

        private async static Task<StorageFolder> CreateDirectory(string path)
        {
            return await CreateDirectory(ApplicationData.Current.LocalFolder, path);
        }

        private async static Task<StorageFolder> CreateDirectory(StorageFolder folder, string path)
        {
            return await folder.CreateFolderAsync(path, CreationCollisionOption.OpenIfExists);
        }
    }
}
