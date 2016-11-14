﻿using UnityEngine;
using System.IO;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using ICSharpCode.SharpZipLib.Zip;

[System.Serializable]
public class GameSync : MonoBehaviour {

    public Text syncDescription;

    private string api_key;
    private string library_url;
    private string games_dir;

    private ArrayList playlists = new ArrayList();

    private void initConfig() {
        api_key = GM.options.GetSyncSettings()["api_key"];
        library_url = GM.options.GetSyncSettings()["library_url"];
        games_dir = GM.options.playlistsPath;
    }

    public void execute() {
        GM.dbug.Log(this, "----------------------------- Running sync -----------------------------");
        SyncText("INITIALIZING SYNC!");

        initConfig();
        fetchPlaylistSubscriptions();
    }

    private void fetchPlaylistSubscriptions() {
        WWW www = new WWW(library_url + "/api/v1/playlists/?api_key=" + api_key);

        StartCoroutine(WaitForSubscriptionList(www));
    }

    private void EndSync()
    {
        //Change state back to intro when completed
        GM.state.ChangeState(StateManager.WorldState.Intro);
    }

    private IEnumerator WaitForSubscriptionList(WWW www) {

        yield return www;

        if (www.error == null) {
            GM.dbug.Log(this, "fetched playlists: " + www.text);
            var data = JSON.Parse(www.text);
            foreach (JSONNode playlist_data in data["playlists"].AsArray) {
                playlists.Add(new Playlist(playlist_data, games_dir));
            }

            // delete unsubscribed playlists
            SluggedItem.deleteExtraDirectories(games_dir, playlists);

            foreach(Playlist playlist in playlists) {
                GM.dbug.Log(this, "creating " + playlist.parentDirectory);

                SyncText("Initializing playlist " + playlist.title);

                Directory.CreateDirectory(playlist.parentDirectory);
                playlist.deleteRemovedGames();
                ArrayList games = playlist.gamesToInstall();

                foreach (Game game in games) {
                    GM.dbug.Log(this, "Downloading: " + game.title);


                    //Start the downloadin'!

                    WWW download = new WWW(game.downloadURL);

                    while (!download.isDone)
                    {
                        int progress = Mathf.FloorToInt(download.progress * 100);
                        SyncText("Downloading " + game.title + " %" + progress);
                        yield return null;
                    }


                    //Download complete!

                    if (!string.IsNullOrEmpty(download.error))
                    {
                        // error!
                        Debug.LogError("Error downloading '" + download.url + "': " + download.error);
                        yield return SyncText("Error downloading " + game.title + "!", 3);
                    }
                    else
                    {
                        //Things downloaded fine!  Do fun stuff now pls thx.

                        yield return SyncText("Unzipping " + game.title + "...", 0.1f);

                        Directory.CreateDirectory(game.installDirectory);
                        string zipFile = game.installDirectory + "/" + game.slug + ".zip";
                        File.WriteAllBytes(zipFile, download.bytes);

                        FastZip zip = new FastZip();
                        ZipConstants.DefaultCodePage = 0;

                        //Starts the unzip coroutine and waits till it's done
                        zip.ExtractZip(zipFile, game.installDirectory, null);


                        // Download the image
                        WWW imageDownload = new WWW(game.imageURL);
                        while (!imageDownload.isDone) {
                            SyncText("Downloading cover image");
                            yield return null;
                        }

                        System.Uri uri = new System.Uri(game.imageURL);
                        string imageFilename = Path.GetFileName(uri.AbsolutePath);
                        File.WriteAllBytes(game.installDirectory + "/" + imageFilename, imageDownload.bytes);

                        game.writeMetadataFile();
                        File.Delete(zipFile);
                    }
                }

                EndSync();
            }

        } else {
            Debug.LogError("Error fetching playlists: " + www.error);
        }
    }

    private void SyncText(string text)
    {
        syncDescription.text = text;
    }

    private IEnumerator SyncText(string text, float timeToWait)
    {
        SyncText(text);
        yield return new WaitForSeconds(timeToWait);
    }


    private class SluggedItem {
        public string title;
        public string slug;


        public static void deleteExtraDirectories(string parent, ArrayList keepers) {
            if (!Directory.Exists(parent)) // just in case. avoid crash.
                return;

            string[] installed = Directory.GetDirectories(parent);

            foreach (string dir_full_path in installed) {
                string directory = new DirectoryInfo(dir_full_path).Name;

                if (SluggedItem.directoryIsDeletable(directory, keepers)) {
                    Debug.Log("deleting " + dir_full_path);
                    Directory.Delete(dir_full_path, true);
                }
            }
        }

        // Careful here that you don't pass in a full path as `directory`
        private static bool directoryIsDeletable(string directory, ArrayList keepers) {
            // Playlist or Game directories that start with an underscore are local additions,
            // and won't be deleted just because they're not in the website data.
            // In the future it'd be better to have that be a setting in the options or metadata json or something.
            if (directory[0] == '_')
                return false;

            foreach (SluggedItem keeper in keepers) {
                if (directory == keeper.slug)
                    return false;
            }

            return true;
        }
    }

    private class Playlist : SluggedItem {
        public ArrayList games = new ArrayList();
        public string parentDirectory;

        public Playlist(JSONNode data, string parentDir) {
            title = data["title"];
            slug = data["slug"];
            parentDirectory = parentDir;

            foreach(JSONNode game_data in data["games"].AsArray) {
                games.Add(new Game(game_data, parentDirectory + "/" + slug + "/"));
            }
        }

        public ArrayList gamesToInstall() {
            Debug.Log("Syncing games for playlist '" + title + "'");


            ArrayList toInstall = new ArrayList();
            foreach (Game game in games) {
                System.DateTime installModified = new System.DateTime(1982, 2, 2);

                if (game.alreadyInstalled()) {
                    installModified = System.DateTime.Parse(game.installationMetadata ["last_modified"], null, System.Globalization.DateTimeStyles.RoundtripKind);
                }

                if (!game.alreadyInstalled() || game.lastModified > installModified) {
                    toInstall.Add(game);
                }
            }

            return toInstall;
        }

        public void deleteRemovedGames() {
            SluggedItem.deleteExtraDirectories(parentDirectory + "/" + slug, games);
        }




    }

    private class Game : SluggedItem {
        public string description;
        public string downloadURL;
        public System.DateTime lastModified;
        public string installDirectory;
        public string imageURL;
        public int minPlayers;
        public int maxPlayers;
        public string executable;
        public bool legacyControls;

        public JSONNode installationMetadata;

        public Game(JSONNode data, string parentDir) {
            title = data["title"];
            slug = data["slug"];
            description = data["description"];
            downloadURL = data["download_url"];
            lastModified = System.DateTime.Parse(data["last_modified"], null, System.Globalization.DateTimeStyles.RoundtripKind);
            installDirectory = parentDir + slug + "/";
            minPlayers = data["min_players"].AsInt;
            maxPlayers = data["max_players"].AsInt;
            executable = data["executable"];
            legacyControls = data["legacy_controls"].AsBool;
            imageURL = data["image_url"];

            if (alreadyInstalled()) {
                string json = System.IO.File.ReadAllText(installDirectory + "winnitron_metadata.json");
                installationMetadata = JSON.Parse(json);
            } else {
                installationMetadata = new JSONClass();
            }
        }


        public bool alreadyInstalled() {
            Debug.Log(installDirectory);
            return Directory.Exists(installDirectory);
        }

        public void writeMetadataFile() {

            installationMetadata.Add("title", new JSONData(title));
            installationMetadata.Add("slug", new JSONData(slug));
            //installationMetadata.Add("description", new JSONData(description)); // TODO buggy for some reason? Blank?
            installationMetadata.Add("last_modified", new JSONData(System.DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture)));
            installationMetadata.Add("min_players", new JSONData(minPlayers));
            installationMetadata.Add("max_players", new JSONData(maxPlayers));
            installationMetadata.Add("executable", new JSONData(executable));
            installationMetadata.Add("legacy_controls", new JSONData(legacyControls));
            installationMetadata.Add("image_url", new JSONData(imageURL));

            string filename = installDirectory + "/winnitron_metadata.json";
            Debug.Log("writing to " + filename + ": " + installationMetadata.ToString());
            System.IO.File.WriteAllText(filename, installationMetadata.ToString());
        }
    }
}

