#region GNU license
// MP-TVSeries - Plugin for Mediaportal
// http://www.team-mediaportal.com
// Copyright (C) 2006-2007
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//
//+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
//+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using MediaPortal.GUI.Video;
using MediaPortal.Player;
using MediaPortal.Dialogs;
using MediaPortal.Util;
using MediaPortal.Playlists;
using MediaPortal.Video.Database;
using WindowPlugins.GUITVSeries;


namespace WindowPlugins.GUITVSeries
{
    class VideoHandler
    {
        #region Vars
        static MediaPortal.Playlists.PlayListPlayer playlistPlayer;
        DBEpisode m_currentEpisode;
        DBEpisode m_previousEpisode;
        System.ComponentModel.BackgroundWorker w = new System.ComponentModel.BackgroundWorker();
        public delegate void rateRequest(DBEpisode episode);
        public event rateRequest RateRequestOccured;
        private bool m_bIsExternalPlayer = false;
        private bool m_bIsExternalDVDPlayer = false;
        private bool m_bIsImageFile = false;
		private bool listenToExternalPlayerEvents = false;
        #endregion

        #region Constructor
        public VideoHandler()
        {
            playlistPlayer = MediaPortal.Playlists.PlayListPlayer.SingletonPlayer;

            // Check if External Player is being used
            MediaPortal.Profile.Settings xmlreader = new MediaPortal.Profile.Settings(Config.GetFile(Config.Dir.Config, "MediaPortal.xml"));
            m_bIsExternalPlayer = !xmlreader.GetValueAsBool("movieplayer", "internal", true);
            m_bIsExternalDVDPlayer = !xmlreader.GetValueAsBool("dvdplayer", "internal", true);
            
			// external player handlers
			MediaPortal.Util.Utils.OnStartExternal += new MediaPortal.Util.Utils.UtilEventHandler(onStartExternal);
			MediaPortal.Util.Utils.OnStopExternal += new MediaPortal.Util.Utils.UtilEventHandler(onStopExternal);

            g_Player.PlayBackStopped += new MediaPortal.Player.g_Player.StoppedHandler(OnPlayBackStopped);
            g_Player.PlayBackEnded += new MediaPortal.Player.g_Player.EndedHandler(OnPlayBackEnded);
            g_Player.PlayBackStarted += new MediaPortal.Player.g_Player.StartedHandler(OnPlayBackStarted);
            g_Player.PlayBackChanged += new g_Player.ChangedHandler(OnPlaybackChanged);
            w.WorkerSupportsCancellation = true;
            w.DoWork += new System.ComponentModel.DoWorkEventHandler(w_DoWork);
        }

        #endregion

        #region Public Methods
        public bool ResumeOrPlay(DBEpisode episode)
        {
            try
            {
                MPTVSeriesLog.Write("Attempting to play: ", episode[DBEpisode.cFilename].ToString(), MPTVSeriesLog.LogLevel.Debug);
                // don't have this file !
                if (episode[DBEpisode.cFilename].ToString().Length == 0)
                    return false;

                m_previousEpisode = m_currentEpisode;
                m_currentEpisode = episode;
                int timeMovieStopped = m_currentEpisode[DBEpisode.cStopTime];

                // Check if file is an Image e.g. ISO
                string filename = m_currentEpisode[DBEpisode.cFilename];
                m_bIsImageFile = Helper.IsImageFile(filename);

                #region Invoke Before Playback
                // see if we have an invokeOption set up
                string invoke = (string)DBOption.GetOptions(DBOption.cInvokeExtBeforePlayback);                
                if (!string.IsNullOrEmpty(invoke))
                {
                    string invokeArgs = (string)DBOption.GetOptions(DBOption.cInvokeExtBeforePlaybackArgs);
                    try
                    {
                        // replace any placeholders in the arguments for the script if any have been supplied.
                        if (!string.IsNullOrEmpty(invokeArgs))
                        {
                            invokeArgs = FieldGetter.resolveDynString(invokeArgs, m_currentEpisode, true);
                        }
                        invoke = FieldGetter.resolveDynString(invoke, m_currentEpisode, true);
                        
                        // use ProcessStartInfo instead of Process.Start(string) as latter produces a "cannot find file"
                        // error if you pass in command line arguments.
                        // also allows us to run the script hidden, preventing, for example, a command prompt popping up.
                        ProcessStartInfo psi = new ProcessStartInfo(invoke, invokeArgs);
                        psi.WindowStyle = ProcessWindowStyle.Hidden;
                        Process proc = System.Diagnostics.Process.Start(psi);
                        MPTVSeriesLog.Write(string.Format("Sucessfully Invoked BeforeFilePlay Command: '{0}' '{1}'",  invoke, invokeArgs));

                        // if not present in database this evaluates to false. If present and not a valid bool then
                        // it evaluates to true
                        bool waitForExit = (bool)DBOption.GetOptions(DBOption.cInvokeExtBeforePlaybackWaitForExit);
                        
                        // if true this thread will wait for the external user script to complete before continuing.
                        if (waitForExit)
                        {
                            proc.WaitForExit();
                        }
                    }
                    catch (Exception e)
                    {
                        MPTVSeriesLog.Write(string.Format("Unable to Invoke BeforeFilePlay Command: '{0}' '{1}'",  invoke, invokeArgs));
                        MPTVSeriesLog.Write(e.Message);
                    }
                }
                #endregion

                #region Removable Media Handling                
                if (!File.Exists(m_currentEpisode[DBEpisode.cFilename]))
                {
                    string episodeVolumeLabel = m_currentEpisode[DBEpisode.cVolumeLabel].ToString();

                    if (string.IsNullOrEmpty(episodeVolumeLabel))
                        episodeVolumeLabel = LocalParse.getImportPath(m_currentEpisode[DBEpisode.cFilename]);

                    // ask the user to input cd/dvd, usb disk or confirm network drive is connected
                    GUIDialogOK dlgOK = (GUIDialogOK)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_OK);
                    if (null == dlgOK)
                        return false;
                    dlgOK.SetHeading(Translation.insertDisk);
                    dlgOK.SetLine(1, Translation.InsertDiskMessage1);
                    dlgOK.SetLine(2, Translation.InsertDiskMessage2);
                    dlgOK.SetLine(3, Translation.InsertDiskMessage3);
                    dlgOK.SetLine(4, string.Format(Translation.InsertDiskMessage4, episodeVolumeLabel));                    
                    dlgOK.DoModal(GUIWindowManager.ActiveWindow);

                    if (!File.Exists(m_currentEpisode[DBEpisode.cFilename]))
                    {
                        return false; // still not found, return to list
                    }

                }
                #endregion

                #region Ask user to Resume

                // skip this if we are using an External Player                
                bool bExternalPlayer = m_bIsImageFile ? m_bIsExternalDVDPlayer : m_bIsExternalPlayer;
                
                if (timeMovieStopped > 0 && !bExternalPlayer) {                                       
                    MPTVSeriesLog.Write("Asking user to resume episode from: " + Utils.SecondsToHMSString(timeMovieStopped));
                    GUIDialogYesNo dlgYesNo = (GUIDialogYesNo)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_YES_NO);

                    if (null != dlgYesNo) {
                        dlgYesNo.SetHeading(GUILocalizeStrings.Get(900)); //resume movie?
                        dlgYesNo.SetLine(1, m_currentEpisode.onlineEpisode.CompleteTitle);
                        dlgYesNo.SetLine(2, GUILocalizeStrings.Get(936) + " " + Utils.SecondsToHMSString(timeMovieStopped));
                        dlgYesNo.SetDefaultToYes(true);
                        dlgYesNo.DoModal(GUIWindowManager.ActiveWindow);
                        // reset resume data in DB
                        if (!dlgYesNo.IsConfirmed) {
                            timeMovieStopped = 0;
                            m_currentEpisode[DBEpisode.cStopTime] = timeMovieStopped;
                            m_currentEpisode.Commit();
                            MPTVSeriesLog.Write("User selected to start episode from beginning", MPTVSeriesLog.LogLevel.Debug);
                        }
                        else {
                            MPTVSeriesLog.Write("User selected to resume episode", MPTVSeriesLog.LogLevel.Debug);
                        }
                    }                
                }

                #endregion

                Play(timeMovieStopped);
                return true;
            }
            catch (Exception e)
            {
                MPTVSeriesLog.Write("TVSeriesPlugin.VideoHandler.ResumeOrPlay()\r\n" + e.ToString());
                return false;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>        
        /// Updates the movie metadata on the playback screen (for when the user clicks info). 
        /// The delay is neccesary because Player tries to use metadata from the MyVideos database.
        /// We want to update this after that happens so the correct info is there.
        /// Clears properties if (EventArgs.Argument == true)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void w_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            bool clear = (bool)e.Argument;
            if (!clear)
                System.Threading.Thread.Sleep(2000);

            if (w.CancellationPending) return;
            
            SetGUIProperties((bool)e.Argument);
        }

        /// <summary>
        /// Sets the following Properties:
        /// "#Play.Current.Title"
        /// "#Play.Current.Plot"
        /// "#Play.Current.Thumb"
        /// "#Play.Current.Year"
        /// </summary>
        /// <param name="clear">Clears the properties instead of filling them if True</param>
        void SetGUIProperties(bool clear)
        {
            if (m_currentEpisode == null) return;

            DBSeries series = null;
            if (!clear) series = Helper.getCorrespondingSeries(m_currentEpisode[DBEpisode.cSeriesID]);
            DBSeason season = null;
            if (!clear) season = Helper.getCorrespondingSeason(m_currentEpisode[DBEpisode.cSeriesID], m_currentEpisode[DBEpisode.cSeasonIndex]);

			// Show Plot in OSD or Hide Spoilers (note: FieldGetter takes care of that)         
            GUIPropertyManager.SetProperty("#Play.Current.Plot", clear ? " " : FieldGetter.resolveDynString(TVSeriesPlugin.m_sFormatEpisodeMain, m_currentEpisode));

			// Show Episode Thumbnail or Series Poster if Hide Spoilers is enabled
            string osdImage = string.Empty;
            //bool hiddenEpLogo = false;
            if (!clear)
            {
                foreach (KeyValuePair<string, string> kvp in SkinSettings.VideoOSDImages)
                {
                    switch (kvp.Key) 
                    {
                        case "episode":
                            if (!DBOption.GetOptions(DBOption.cView_Episode_HideUnwatchedThumbnail) || m_currentEpisode[DBOnlineEpisode.cWatched])
                                osdImage = ImageAllocator.ExtractFullName(localLogos.getFirstEpLogo(m_currentEpisode));
                            break;
                        case "season":
                            osdImage = season.Banner;
                            break;
                        case "series":
                            osdImage = series.Poster;
                            break;
                        case "custom":
                            string value = replaceDynamicFields(kvp.Value);
                            string file = Helper.getCleanAbsolutePath(value);
                            if (System.IO.File.Exists(file))
                                osdImage = file;
                            break;
                    }

                    osdImage = osdImage.Trim();
                    if (string.IsNullOrEmpty(osdImage)) continue;
                    else break;
                }
            }
            GUIPropertyManager.SetProperty("#Play.Current.Thumb", clear ? " " : osdImage);

            // double check, i don't want play images to be cleared on ended or stopped...
            if (w.CancellationPending) return;

            foreach (KeyValuePair<string, string> kvp in SkinSettings.VideoPlayImages)
            {
                if (!clear)
                {
                    string value = replaceDynamicFields(kvp.Value);
                    string file = Helper.getCleanAbsolutePath(value);
                    if (System.IO.File.Exists(file))
                    {
                        MPTVSeriesLog.Write(string.Format("Setting play image {0} for property {1}", file, kvp.Key), MPTVSeriesLog.LogLevel.Debug);
                        GUIPropertyManager.SetProperty(kvp.Key, clear ? " " : file);
                    }
                }
                else
                {
                    MPTVSeriesLog.Write(string.Format("Clearing play image for property {0}", kvp.Key), MPTVSeriesLog.LogLevel.Debug);
                    GUIPropertyManager.SetProperty(kvp.Key, " ");
                }
            }
			
            GUIPropertyManager.SetProperty("#Play.Current.Title", clear ? " " : m_currentEpisode.onlineEpisode.CompleteTitle);            
            GUIPropertyManager.SetProperty("#Play.Current.Year", clear ? " " : (string)m_currentEpisode[DBOnlineEpisode.cFirstAired]);                        
            GUIPropertyManager.SetProperty("#Play.Current.Genre", clear ? " " : FieldGetter.resolveDynString(TVSeriesPlugin.m_sFormatEpisodeSubtitle, m_currentEpisode));
        }

        string replaceDynamicFields(string value)
        {
            string result = value;

            Regex matchRegEx = new Regex(@"\<[a-zA-Z\.]+\>");
            foreach (Match m in matchRegEx.Matches(value))
            {
                string resolvedValue = FieldGetter.resolveDynString(m.Value, m_currentEpisode, false);
                result = result.Replace(m.Value, resolvedValue);
            }

            return result;
        }

        void MarkEpisodeAsWatched(DBEpisode episode)
        {
            // Could be a double episode, so mark both as watched
            SQLCondition condition = new SQLCondition();
            condition.Add(new DBEpisode(), DBEpisode.cFilename, episode[DBEpisode.cFilename], SQLConditionType.Equal);
            List<DBEpisode> episodes = DBEpisode.Get(condition, false);
            foreach (DBEpisode ep in episodes)
            {
                ep[DBOnlineEpisode.cWatched] = 1;
                ep.Commit();
                //DBSeason.UpdateUnWatched(ep);
                //DBSeries.UpdateUnWatched(ep);
            }
            // Update Episode Counts
            DBSeries series = Helper.getCorrespondingSeries(m_currentEpisode[DBEpisode.cSeriesID]);
            DBSeason season = Helper.getCorrespondingSeason(episode[DBEpisode.cSeriesID], episode[DBEpisode.cSeasonIndex]);
            DBSeason.UpdateEpisodeCounts(series, season);           
        }

        /// <summary>
        /// Initiates Playback of m_currentEpisode[DBEpisode.cFilename] and calls Fullscreen Window
        /// </summary>
        /// <param name="timeMovieStopped">Resumepoint of Movie, 0 or negative for Start from Beginning</param>
        /// 
        bool Play(int timeMovieStopped)
        {
            bool result = false;
            try
            {
                // sometimes it takes up to 30+ secs to go to fullscreen even though the video is already playing
                // lets force fullscreen here
                // note: MP might still be unresponsive during this time, but at least we are in fullscreen and can see video should this happen
                // I haven't actually found out why it happens, but I strongly believe it has something to do with the video database and the player doing something in the background
                // (why does it do anything with the video database.....i just want it to play a file and do NOTHING else!)                
                GUIGraphicsContext.IsFullScreenVideo = true;
                GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_FULLSCREEN_VIDEO);

                // If the file is an image file, it should be mounted before playing
                string filename = m_currentEpisode[DBEpisode.cFilename];
                if (m_bIsImageFile) { 
                    if (!GUIVideoFiles.MountImageFile(GUIWindowManager.ActiveWindow, filename)) {                        
                        return false;
                    }
                }
                
                // Start Listening to any External Player Events
                listenToExternalPlayerEvents = true;

                #region Publish Play properties for InfoService plugin
                string seriesName = Helper.getCorrespondingSeries(m_currentEpisode[DBEpisode.cSeriesID]).ToString();
                string seasonID = m_currentEpisode[DBEpisode.cSeasonIndex];
                string episodeID = m_currentEpisode[DBEpisode.cEpisodeIndex];
                string episodeName = m_currentEpisode[DBEpisode.cEpisodeName];
                GUIPropertyManager.SetProperty("#TVSeries.Extended.Title", string.Format("{0}/{1}/{2}/{3}", seriesName, seasonID, episodeID, episodeName));
                MPTVSeriesLog.Write(string.Format("#TVSeries.Extended.Title: {0}/{1}/{2}/{3}", seriesName, seasonID, episodeID, episodeName));
                #endregion

                // Play File
                result = g_Player.Play(filename, g_Player.MediaType.Video);
                
                // Stope Listening to any External Player Events
				listenToExternalPlayerEvents = false;

                // tell player where to resume                
				if (g_Player.Playing && timeMovieStopped > 0) {
					MPTVSeriesLog.Write("Setting seek position at: " + timeMovieStopped, MPTVSeriesLog.LogLevel.Debug);
					GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SEEK_POSITION, 0, 0, 0, 0, 0, null);
					msg.Param1 = (int)timeMovieStopped;
					GUIGraphicsContext.SendMessage(msg);
				}			

            }
            catch (Exception e)
            {
                MPTVSeriesLog.Write("TVSeriesPlugin.VideoHandler.Play()\r\n" + e.ToString());
                result = false;
            }
            return result;
        }
        #endregion

        #region Playback Event Handlers
        void OnPlayBackStopped(MediaPortal.Player.g_Player.MediaType type, int timeMovieStopped, string filename)
        {
            if (PlayBackOpIsOfConcern(type, filename))
            {
                if (w.IsBusy) w.CancelAsync();
                LogPlayBackOp("stopped", filename);
                try
                {
                    #region Set Resume Point or Watched                    
                    double watchedAfter = DBOption.GetOptions(DBOption.cWatchedAfter);
                    if (!m_currentEpisode[DBOnlineEpisode.cWatched]
                        && (timeMovieStopped / playlistPlayer.g_Player.Duration) > watchedAfter / 100)
                    {
                        m_currentEpisode[DBEpisode.cStopTime] = 0;
                        m_currentEpisode.Commit();
                        PlaybackOperationEnded(true);
                    }
                    else
                    {
                        m_currentEpisode[DBEpisode.cStopTime] = timeMovieStopped;
                        m_currentEpisode.Commit();
                        PlaybackOperationEnded(false);                        
                    }
                    #endregion

                    m_currentEpisode = null;
                    m_previousEpisode = null;
                }
                catch (Exception e)
                {
                    MPTVSeriesLog.Write("TVSeriesPlugin.VideoHandler.OnPlayBackStopped()\r\n" + e.ToString());
                }
            }
        }

        void OnPlayBackEnded(MediaPortal.Player.g_Player.MediaType type, string filename)
        {
            if (PlayBackOpIsOfConcern(type, filename))
            {
                if (w.IsBusy) w.CancelAsync();
                LogPlayBackOp("ended", filename);
                try
                {
                    m_currentEpisode[DBEpisode.cStopTime] = 0;
                    m_currentEpisode.Commit();
                    PlaybackOperationEnded(true);

                    m_currentEpisode = null;
                    m_previousEpisode = null;
                }
                catch (Exception e)
                {
                    MPTVSeriesLog.Write("TVSeriesPlugin.VideoHandler.OnPlayBackEnded()\r\n" + e.ToString());
                }
            }
        }

        void OnPlaybackChanged(g_Player.MediaType type, int timeMovieStopped, string filename)
        {
            if (PlayBackOpWasOfConcern(g_Player.IsVideo? g_Player.MediaType.Video : g_Player.MediaType.Unknown, g_Player.CurrentFile))
            {
                if (w.IsBusy) w.CancelAsync();
                LogPlayBackOp("changed", g_Player.CurrentFile);
                try 
                {
                    #region Set Resume Point or Watched
                    double watchedAfter = DBOption.GetOptions(DBOption.cWatchedAfter);
                    if (!m_previousEpisode[DBOnlineEpisode.cWatched]
                        && (timeMovieStopped / playlistPlayer.g_Player.Duration) > watchedAfter / 100) 
                    {
                        m_previousEpisode[DBEpisode.cStopTime] = 0;
                        m_previousEpisode.Commit();
                        MPTVSeriesLog.Write("This episode counts as watched");
                        MarkEpisodeAsWatched(m_previousEpisode);
                        SetGUIProperties(true);
                    }
                    else
                    {
                        m_previousEpisode[DBEpisode.cStopTime] = timeMovieStopped;
                        m_previousEpisode.Commit();
                        SetGUIProperties(true);
                    }
                    #endregion

                    m_previousEpisode = null;
                }
                catch (Exception e)
                {
                    MPTVSeriesLog.Write("TVSeriesPlugin.VideoHandler.OnPlaybackChanged()\r\n" + e.ToString());
                }
            }
        }

        void OnPlayBackStarted(MediaPortal.Player.g_Player.MediaType type, string filename)
        {
            if (PlayBackOpIsOfConcern(type, filename))
            {
                LogPlayBackOp("started", filename);
                // really stupid, you have to wait until the player itself sets the properties (a few seconds) and after that set them
                w.RunWorkerAsync(false);
            }
        }
        #endregion

		#region External Player Event Handlers
		private void onStartExternal(Process proc, bool waitForExit) {
			// If we were listening for external player events
			if (listenToExternalPlayerEvents) {
				MPTVSeriesLog.Write("Playback Started in External Player:" + m_currentEpisode.ToString());				
			}
		}

		private void onStopExternal(Process proc, bool waitForExit) {
			if (!listenToExternalPlayerEvents)
				return;

			MPTVSeriesLog.Write("Playback Stopped in External Player:" + m_currentEpisode.ToString());
		
			// Exit fullscreen Video so we can see main facade again			
			if (GUIGraphicsContext.IsFullScreenVideo) {
				GUIGraphicsContext.IsFullScreenVideo = false;
			}
			// Mark Episode as watched regardless and prompt for rating
			bool markAsWatched = (DBOption.GetOptions(DBOption.cWatchedAfter) > 0 && m_currentEpisode[DBOnlineEpisode.cWatched] == 0);
			PlaybackOperationEnded(markAsWatched);	
		}
		#endregion

        #region Helpers
        bool PlayBackOpIsOfConcern(MediaPortal.Player.g_Player.MediaType type, string filename)
        {
            if (string.IsNullOrEmpty(filename)) return false;

            return (m_currentEpisode != null && 
                    type == g_Player.MediaType.Video && 
                    m_currentEpisode[DBEpisode.cFilename] == filename);
        }

        bool PlayBackOpWasOfConcern(MediaPortal.Player.g_Player.MediaType type, string filename) {
            if (string.IsNullOrEmpty(filename)) return false;

            return (m_previousEpisode != null &&
                    type == g_Player.MediaType.Video &&
                    m_previousEpisode[DBEpisode.cFilename] == filename);
        }

        void PlaybackOperationEnded(bool countAsWatched)
        {
            if (countAsWatched || m_currentEpisode[DBOnlineEpisode.cWatched])
            {
                MPTVSeriesLog.Write("This episode counts as watched");
                if(countAsWatched) MarkEpisodeAsWatched(m_currentEpisode);
                // if the ep wasn't rated before, and the option to ask is set, bring up the ratings menu
                if ((String.IsNullOrEmpty(m_currentEpisode[DBOnlineEpisode.cMyRating]) || m_currentEpisode[DBOnlineEpisode.cMyRating] == 0) && DBOption.GetOptions(DBOption.cAskToRate))
                {
                    MPTVSeriesLog.Write("Episode not rated yet");
                    if(RateRequestOccured != null)
                        RateRequestOccured.Invoke(m_currentEpisode);
                } else MPTVSeriesLog.Write("Episode has already been rated or option not set");
            }
            SetGUIProperties(true); // clear GUI Properties

            #region Invoke After Playback
            string invoke = (string)DBOption.GetOptions(DBOption.cInvokeExtAfterPlayback);
            if (countAsWatched && !string.IsNullOrEmpty(invoke))
            {
                string invokeArgs = (string)DBOption.GetOptions(DBOption.cInvokeExtAfterPlaybackArgs);
                try
                {                    
                    // replace any placeholders in the arguments for the script if any have been supplied.
                    if (!string.IsNullOrEmpty(invokeArgs))
                    {
                        invokeArgs = FieldGetter.resolveDynString(invokeArgs, m_currentEpisode, true);
                    }
                    invoke = FieldGetter.resolveDynString(invoke, m_currentEpisode, true);

                    // use ProcessStartInfo instead of Process.Start(string) as latter produces a "cannot find file"
                    // error if you pass in command line arguments.
                    // also allows us to run the script hidden, preventing, for example, a command prompt popping up.
                    ProcessStartInfo psi = new ProcessStartInfo(invoke, invokeArgs);
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process proc = System.Diagnostics.Process.Start(psi);
                    MPTVSeriesLog.Write(string.Format("Sucessfully Invoked AfterFilePlay Command: '{0}' '{1}'", invoke, invokeArgs));

                    // if not present in database this evaluates to false. If present and not a valid bool then
                    // it evaluates to true
                    bool waitForExit = (bool)DBOption.GetOptions(DBOption.cInvokeExtAfterPlaybackWaitForExit);

                    // if true this thread will wait for the external user script to complete before continuing.
                    if (waitForExit)
                    {
                        proc.WaitForExit();
                    }
                }
                catch (Exception e)
                {
                    MPTVSeriesLog.Write(string.Format("Unable to Invoke ExtAfterPlayback Command: '{0}' '{1}'", invoke, invokeArgs));
                    MPTVSeriesLog.Write(e.Message);
                }
            }
            #endregion
        }

        void LogPlayBackOp(string OperationType, string filename)
        {
            MPTVSeriesLog.Write(string.Format("Playback {0} for: {1}", OperationType, filename), MPTVSeriesLog.LogLevel.Normal);
        }        

        #endregion
    }
}