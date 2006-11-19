using System;
using System.Windows.Forms;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.Dialogs;
using MediaPortal.Util;
using MediaPortal.Playlists;
using WindowPlugins.GUITVSeries;
using System.Threading;

namespace MediaPortal.GUI.Video
{
    public class TVSeriesPlugin : GUIWindow, ISetupForm
    {
        public TVSeriesPlugin()
        {
            //
            // TODO: Add constructor logic here
            //
        }
        #region ISetupForm Members

        // Returns the name of the plugin which is shown in the plugin menu
        public string PluginName()
        {
            return "MP-TV Series";
        }

        // Returns the description of the plugin is shown in the plugin menu
        public string Description()
        {
            return "Plugin used to manage and play television series";
        }

        // Returns the author of the plugin which is shown in the plugin menu
        public string Author()
        {
            return "Zeflash, based on the work of WeeToddDid (Luc Theriault)";
        }

        // show the setup dialog
        public void ShowPlugin()
        {
            ConfigurationForm dialog = new ConfigurationForm();
            dialog.ShowDialog();
        }

        // Indicates whether plugin can be enabled/disabled
        public bool CanEnable()
        {
            return true;
        }

        // get ID of windowplugin belonging to this setup
        public int GetWindowId()
        {
            return 9811;
        }

        // Indicates if plugin is enabled by default;
        public bool DefaultEnabled()
        {
            return true;
        }

        // indicates if a plugin has its own setup screen
        public bool HasSetup()
        {
            return true;
        }

        /// <summary>
        /// If the plugin should have its own button on the main menu of Media Portal then it
        /// should return true to this method, otherwise if it should not be on home
        /// it should return false
        /// </summary>
        /// <param name="strButtonText">text the button should have</param>
        /// <param name="strButtonImage">image for the button, or empty for default</param>
        /// <param name="strButtonImageFocus">image for the button, or empty for default</param>
        /// <param name="strPictureImage">subpicture for the button or empty for none</param>
        /// <returns>true  : plugin needs its own button on home
        ///          false : plugin does not need its own button on home</returns>
        public bool GetHome(out string strButtonText, out string strButtonImage, out string strButtonImageFocus, out string strPictureImage)
        {
            strButtonText = "MP-TVSeries";
            strButtonImage = String.Empty;
            strButtonImageFocus = String.Empty;
            strPictureImage = "hover_my tv series.png";
            return true;
        }

        #endregion

        private const String cListLevelSeries = "Series";
        private const String cListLevelSeasons = "Seasons";
        private const String cListLevelEpisodes = "Episodes";
        private String m_ListLevel = cListLevelSeries;
        private DBSeries m_SelectedSeries;
        private DBSeason m_SelectedSeason;
        private DBEpisode m_SelectedEpisode;
        private VideoHandler m_VideoHandler;

        private TimerCallback timerDelegate = null;
        private System.Threading.Timer scanTimer = null;
        private OnlineParsing m_parserUpdater = null;
        private int m_nLocalScanLapse = 0;
        private int m_nUpdateScanLapse = 0;
        private DateTime m_LastLocalScan = DateTime.MinValue;
        private DateTime m_LastUpdateScan = DateTime.MinValue;
        private WaitCursor m_waitCursor = null;

        private String m_sFormatSeriesCol1 = String.Empty;
        private String m_sFormatSeriesCol2 = String.Empty;
        private String m_sFormatSeriesCol3 = String.Empty;
        private String m_sFormatSeriesTitle = String.Empty;
        private String m_sFormatSeriesSubtitle = String.Empty;
        private String m_sFormatSeriesMain = String.Empty;

        private String m_sFormatEpisodeCol1 = String.Empty;
        private String m_sFormatEpisodeCol2 = String.Empty;
        private String m_sFormatEpisodeCol3 = String.Empty;
        private String m_sFormatEpisodeTitle = String.Empty;
        private String m_sFormatEpisodeSubtitle = String.Empty;
        private String m_sFormatEpisodeMain = String.Empty;
        
#region Skin Variables
        [SkinControlAttribute(2)]
        protected GUIButtonControl m_Button_Back = null;

        [SkinControlAttribute(3)]
        protected GUIButtonControl m_Button_View = null;

        [SkinControlAttribute(50)]
        protected GUIFacadeControl m_Facade = null;

        [SkinControlAttribute(30)]
        protected GUIImage m_Image = null;

        [SkinControlAttribute(31)]
        protected GUITextScrollUpControl m_Description = null;

        [SkinControlAttribute(32)]
        protected GUITextScrollUpControl m_Series_Name = null;

        [SkinControlAttribute(33)]
        protected GUITextScrollUpControl m_Genre = null;

        [SkinControlAttribute(34)]
        protected GUITextControl m_Series_Network = null;

        [SkinControlAttribute(35)]
        protected GUITextControl m_Series_Duration = null;

        [SkinControlAttribute(36)]
        protected GUITextControl m_Series_Status = null;

        [SkinControlAttribute(37)]
        protected GUITextControl m_Series_Premiered = null;

        [SkinControlAttribute(40)]
        protected GUITextControl m_Title = null;

        [SkinControlAttribute(41)]
        protected GUITextControl m_Airs = null;

        [SkinControlAttribute(42)]
        protected GUITextControl m_Episode_SeasonNumber = null;

        [SkinControlAttribute(43)]
        protected GUITextControl m_Episode_EpisodeNumber = null;

        [SkinControlAttribute(44)]
        protected GUITextControl m_Episode_Filename = null;

        [SkinControlAttribute(45)]
        protected GUITextControl m_Episode_Actors = null;

        [SkinControlAttribute(46)]
        protected GUIImage m_Season_Image = null;
        #endregion

        public override int GetID
        {
            get
            {
                return 9811;
            }
        }

        public override bool Init()
        {
            MPTVSeriesLog.Write("**** Plugin started in MediaPortal ***");
            String xmlSkin = GUIGraphicsContext.Skin + @"\TVSeries.xml";
            MPTVSeriesLog.Write("Loading XML Skin: " + xmlSkin);

            m_VideoHandler = new VideoHandler();

            // init display format strings
            m_sFormatSeriesCol1 = DBOption.GetOptions(DBOption.cView_Series_Col1);
            m_sFormatSeriesCol2 = DBOption.GetOptions(DBOption.cView_Series_Col2);
            m_sFormatSeriesCol3 = DBOption.GetOptions(DBOption.cView_Series_Col3);
            m_sFormatSeriesTitle = DBOption.GetOptions(DBOption.cView_Series_Title);
            m_sFormatSeriesSubtitle = DBOption.GetOptions(DBOption.cView_Series_Subtitle);
            m_sFormatSeriesMain = DBOption.GetOptions(DBOption.cView_Series_Main);

            m_sFormatEpisodeCol1 = DBOption.GetOptions(DBOption.cView_Episode_Col1);
            m_sFormatEpisodeCol2 = DBOption.GetOptions(DBOption.cView_Episode_Col2);
            m_sFormatEpisodeCol3 = DBOption.GetOptions(DBOption.cView_Episode_Col3);
            m_sFormatEpisodeTitle = DBOption.GetOptions(DBOption.cView_Episode_Title);
            m_sFormatEpisodeSubtitle = DBOption.GetOptions(DBOption.cView_Episode_Subtitle);
            m_sFormatEpisodeMain = DBOption.GetOptions(DBOption.cView_Episode_Main);

            try
            {
                m_LastLocalScan = DateTime.Parse(DBOption.GetOptions(DBOption.cLocalScanLastTime));
            }
            catch {}
            try
            {
                m_LastUpdateScan = DateTime.Parse(DBOption.GetOptions(DBOption.cUpdateScanLastTime));
            }
            catch {}

            if (DBOption.GetOptions(DBOption.cAutoScanLocalFiles))
                m_nLocalScanLapse = DBOption.GetOptions(DBOption.cAutoScanLocalFilesLapse);
            if (DBOption.GetOptions(DBOption.cAutoUpdateOnlineData))
                m_nUpdateScanLapse = DBOption.GetOptions(DBOption.cAutoUpdateOnlineDataLapse);

            // timer check every 10 seconds
            timerDelegate = new TimerCallback(Clock);
            scanTimer = new System.Threading.Timer(timerDelegate, null, 10000, 10000);
            return Load(xmlSkin);
        }

        String FormatField(String sFormat, DBTable table)
        {
            String sOut = String.Empty;

            while (sFormat.Length != 0)
            {
                int nTagStart = sFormat.IndexOf('<');
                if (nTagStart != -1)
                {
                    sOut += sFormat.Substring(0, nTagStart);
                    sFormat = sFormat.Substring(nTagStart);
                    
                    int nTagEnd = sFormat.IndexOf('>');
                    if (nTagEnd != -1)
                    {
                        String sTag = sFormat.Substring(1, nTagEnd - 1);
                        sFormat = sFormat.Substring(nTagEnd + 1);

                        if (sTag.IndexOf('.') != -1)
                        {
                            String sTableName = sTag.Substring(0, sTag.IndexOf('.'));
                            String sFieldName = sTag.Substring(sTag.IndexOf('.') + 1);

                            switch (sTableName)
                            {
                                case DBSeries.cOutName:
                                    {
                                        DBSeries source = table as DBSeries;
                                        if (source == null)
                                            source = m_SelectedSeries;
                                        if (source != null)
                                        {
                                            switch (sFieldName)
                                            {
                                                case DBSeries.cActors:
                                                case DBSeries.cGenre:
                                                    sOut += ((String)source[sFieldName]).Trim('|').Replace("|", ", ");
                                                    break;

                                                default:
                                                    sOut += source[sFieldName];
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case DBSeason.cOutName:
                                    {
                                        DBSeason source = table as DBSeason;
                                        if (source == null)
                                            source = m_SelectedSeason;
                                        if (source != null)
                                        {
                                            sOut += source[sFieldName];
                                        }
                                    }
                                    break;

                                case DBEpisode.cOutName:
                                    {
                                        DBEpisode source = table as DBEpisode;
                                        if (source == null)
                                            source = m_SelectedEpisode;
                                        if (source != null)
                                        {
                                            switch (sFieldName)
                                            {
                                                case DBOnlineEpisode.cEpisodeSummary:
                                                    if (DBOption.GetOptions(DBOption.cView_Episode_HideUnwatchedSummary) != true || table[DBOnlineEpisode.cWatched] != 0)
                                                        sOut += source[sFieldName];
                                                    else
                                                        sOut += " * Hidden to prevent spoilers *";
                                                    break;

                                                case DBOnlineEpisode.cWatched:
                                                    sOut += source[sFieldName] == 0 ? "No" : "Yes";
                                                    break;

                                                case DBOnlineEpisode.cGuestStars:
                                                case DBOnlineEpisode.cDirector:
                                                case DBOnlineEpisode.cWriter:
                                                    sOut += ((String)source[sFieldName]).Trim('|').Replace("|", ", ");
                                                    break;

                                                default:
                                                    sOut += source[sFieldName];
                                                    break;
                                            }
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    else
                    {
                        sOut += "#Error";
                        sFormat = String.Empty;
                    }
                }
                else
                {
                    // no more opening tag
                    sOut += sFormat;
                    sFormat = String.Empty;
                }
            }

            String sCR = "" + (char)10 + (char)13;
            sOut = sOut.Replace("\\n", sCR);
            return sOut;
        }

        void LoadFacade()
        {
            if (this.m_Facade != null)
            {
                this.m_Facade.Clear();
                bool bEmpty = true;
                switch (this.m_ListLevel)
                {
                    case cListLevelSeries:
                        int selectedIndex = -1;
                        int count = 0;
                        foreach (DBSeries series in DBSeries.Get(DBOption.GetOptions(DBOption.cView_Episode_OnlyShowLocalFiles)))
                        {
                            try
                            {
                                bEmpty = false;
                                GUIListItem item = new GUIListItem(FormatField(m_sFormatSeriesCol2, series));
                                item.Label2 = FormatField(m_sFormatSeriesCol3, series);
                                item.Label3 = FormatField(m_sFormatSeriesCol1, series);
                                item.TVTag = series;
//                                 String filename = series.Banner;
//                                 if (filename != String.Empty)
//                                     item.IconImage = item.IconImageBig = filename;
                                item.IsRemote = series[DBSeries.cHasLocalFiles] != 0;
                                item.IsDownloading = true;

                                if (this.m_SelectedSeries != null)
                                {
                                    if (series[DBSeries.cParsedName] == this.m_SelectedSeries[DBSeries.cParsedName])
                                        selectedIndex = count;
                                }
                                else
                                {
                                    // select the first that has a file
                                    if (series[DBSeries.cHasLocalFiles] != 0 && selectedIndex == -1)
                                        selectedIndex = count;
                                }

                                this.m_Facade.Add(item);
                            }
                            catch (Exception ex)
                            {
                                MPTVSeriesLog.Write("The 'LoadFacade' function has generated an error displaying series list item: " + ex.Message);
                            }
                            count++;
                        }
                        if (selectedIndex != -1)
                            this.m_Facade.SelectedListItemIndex = selectedIndex;
                        Series_OnItemSelected(this.m_Facade.SelectedListItem);
                        break;
                    case cListLevelSeasons:
                        selectedIndex = -1;
                        count = 0;
                        if (m_SelectedSeries != null)
                        {
                            String filename = m_SelectedSeries.Banner;
                            if (filename != null)
                            {
                                try
                                {
                                    this.m_Image.SetFileName(filename);
                                    this.m_Image.KeepAspectRatio = true;
                                }
                                catch { }
                            }
                        }

                        foreach (DBSeason season in DBSeason.Get(m_SelectedSeries[DBSeries.cParsedName], DBOption.GetOptions(DBOption.cView_Episode_OnlyShowLocalFiles)))
                        {
                            try
                            {
                                bEmpty = false;
                                GUIListItem item = new GUIListItem("Season " + season[DBSeason.cIndex]);
//                                 String filename = season.Banner;
//                                 if (filename == String.Empty) filename = this.m_SelectedSeries.Banner;
//                                 if (filename != String.Empty)
//                                 {
//                                     item.IconImage = filename;
//                                     item.IconImageBig = filename;
//                                 }
                                item.IsRemote = season[DBSeason.cHasLocalFiles] != 0;
                                item.IsDownloading = true;
                                item.TVTag = season;

                                if (this.m_SelectedSeason != null)
                                {
                                    if (this.m_SelectedSeason[DBSeason.cIndex] == season[DBSeason.cIndex])
                                        selectedIndex = count;
                                }
                                else
                                {
                                    // select the first that has a file
                                    if (season[DBSeries.cHasLocalFiles] != 0 && selectedIndex == -1)
                                        selectedIndex = count;
                                }
                                this.m_Facade.Add(item);
                            }
                            catch (Exception ex)
                            {
                                MPTVSeriesLog.Write("The 'LoadFacade' function has generated an error displaying season list item: " + ex.Message);
                            }
                            count++;
                        }

                        if (selectedIndex != -1)
                            this.m_Facade.SelectedListItemIndex = selectedIndex;
                        this.Season_OnItemSelected(this.m_Facade.SelectedListItem);

                        break;
                    case cListLevelEpisodes:
                        selectedIndex = -1;
                        count = 0;

                        if (m_SelectedSeries != null && this.m_Season_Image != null)
                        {
                            String filename = m_SelectedSeries.Banner;
                            if (filename != null)
                            {
                                try
                                {
                                    this.m_Image.SetFileName(filename);
                                    this.m_Image.KeepAspectRatio = true;
                                }
                                catch { }
                            }
                        }
                        if (m_SelectedSeason != null && this.m_Season_Image != null)
                        {
                            String filename = m_SelectedSeason.Banner;
                            if (filename != null)
                            {
                                // SetFileName needs to be tried/catched, as it can fail because we are on another thread. This sucks, but IMHO it's a design problem 
                                // of Mediaportal: all events should be sent on the main thread.
                                try
                                {
                                    this.m_Season_Image.SetFileName(filename);
                                }
                                catch { }
                            }
                        }

                        foreach (DBEpisode episode in DBEpisode.Get(m_SelectedSeries[DBSeries.cParsedName], m_SelectedSeason[DBSeason.cIndex], DBOption.GetOptions(DBOption.cView_Episode_OnlyShowLocalFiles)))
                        {
                            try
                            {
                                bEmpty = false;
                                GUIListItem item = new GUIListItem(FormatField(m_sFormatEpisodeCol2, episode));
                                item.Label2 = FormatField(m_sFormatEpisodeCol3, episode);
                                item.Label3 = FormatField(m_sFormatEpisodeCol1, episode);
                                item.IsRemote = episode[DBEpisode.cFilename] != "";
                                item.IsDownloading = episode[DBEpisode.cWatched] == 0;
                                item.TVTag = episode;

                                if (this.m_SelectedEpisode != null)
                                {
                                    if (episode[DBEpisode.cEpisodeIndex] == this.m_SelectedEpisode[DBEpisode.cEpisodeIndex])
                                        selectedIndex = count;
                                }
                                else
                                {
                                    // select the first that has a file and is not watched
                                    if (episode[DBEpisode.cFilename] != "" && episode[DBEpisode.cWatched] == 0 && selectedIndex == -1)
                                        selectedIndex = count;
                                }

                                this.m_Facade.Add(item);
                            }
                            catch (Exception ex)
                            {
                                MPTVSeriesLog.Write("The 'LoadFacade' function has generated an error displaying episode list item: " + ex.Message);
                            }
                            count++;
                        }
                        this.m_Button_Back.Focus = false;
                        this.m_Facade.Focus = true;
                        if (selectedIndex != -1)
                            this.m_Facade.SelectedListItemIndex = selectedIndex;
                        this.Episode_OnItemSelected(this.m_Facade.SelectedListItem);

                        break;
                }
                if (bEmpty)
                {
                    GUIListItem item = new GUIListItem("No items!");
                    item.IsRemote = true;
                    this.m_Facade.Add(item);
                }
            }
        }

        protected override void OnPageLoad()
        {
            if (m_parserUpdater != null)
                m_waitCursor = new WaitCursor();
            this.LoadFacade();
            this.m_Button_Back.Focus = false;
            this.m_Facade.Focus = true;
        }

        protected override void OnPageDestroy(int new_windowId)
        {
            if (m_waitCursor != null)
            {
                m_waitCursor.Dispose();
                m_waitCursor = null;
            }

            base.OnPageDestroy(new_windowId);
        }

        protected override void OnShowContextMenu()
        {
            try
            {
                GUIListItem currentitem = this.m_Facade.SelectedListItem;
                if (currentitem == null) return;

                GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
                if (dlg == null) return;
                dlg.Reset();
                dlg.SetHeading(924); // menu
                GUIListItem pItem = null;

                switch (this.m_ListLevel)
                {
                    case cListLevelEpisodes:
                        {
                            DBEpisode episode = (DBEpisode)currentitem.TVTag;
                            pItem = new GUIListItem("Toggle watched flag");
                            dlg.Add(pItem);
                            pItem.ItemId = 1;

                            pItem = new GUIListItem("-------------------------------");
                            dlg.Add(pItem);
                        }
                        break;
                }

                pItem = new GUIListItem("Force Local Scan" + (m_parserUpdater != null ? " (In Progress)" : ""));
                dlg.Add(pItem);
                pItem.ItemId = 100 + 1;

                pItem = new GUIListItem("Force Online Refresh" + (m_parserUpdater != null ? " (In Progress)" : ""));
                dlg.Add(pItem);
                pItem.ItemId = 100 + 1;

                pItem = new GUIListItem("-------------------------------");
                dlg.Add(pItem);

                pItem = new GUIListItem("Only show episodes with a local file (" + (DBOption.GetOptions(DBOption.cView_Episode_OnlyShowLocalFiles) ? "on" : "off") + ")");
                dlg.Add(pItem);
                pItem.ItemId = 100 + 3;

                pItem = new GUIListItem("Hide the episode's summary on unwatched episodes (" + (DBOption.GetOptions(DBOption.cView_Episode_HideUnwatchedSummary)? "on" : "off") + ")");
                dlg.Add(pItem);
                pItem.ItemId = 100 + 4;


                dlg.DoModal(GetID);
                if (dlg.SelectedId == -1) return;

                // specific context settings
                switch (this.m_ListLevel)
                {
                    case cListLevelEpisodes:
                        {
                            switch (dlg.SelectedId)
                            {
                                case 1:
                                    // toggle watched
                                    DBEpisode episode = (DBEpisode)currentitem.TVTag;
                                    episode[DBEpisode.cWatched] = episode[DBEpisode.cWatched] == 0;
//                                     if (episode[DBEpisode.cWatched] == 0)
//                                         episode[DBEpisode.cWatched] = 1;
//                                     else
//                                         episode[DBEpisode.cWatched] = 0;
                                    episode.Commit();
                                    LoadFacade();
                                    break;
                            }
                        }
                        break;
                }

                // global context settings
                switch (dlg.SelectedId)
                {
                    case 100 + 1:
                        if (m_parserUpdater == null) 
                        {
                            // only load the wait cursor if we are in the plugin
                            if (m_Facade != null)
                                m_waitCursor = new WaitCursor();

                            // do scan
                            m_parserUpdater = new OnlineParsing();
                            m_parserUpdater.OnlineParsingCompleted += new OnlineParsing.OnlineParsingCompletedHandler(parserUpdater_OnlineParsingCompleted);
                            m_parserUpdater.Start(true, false);
                        }
                        break;

                    case 100 + 2:
                        if (m_parserUpdater == null)
                        {
                            // only load the wait cursor if we are in the plugin
                            if (m_Facade != null)
                                m_waitCursor = new WaitCursor();

                            // do scan
                            m_parserUpdater = new OnlineParsing();
                            m_parserUpdater.OnlineParsingCompleted += new OnlineParsing.OnlineParsingCompletedHandler(parserUpdater_OnlineParsingCompleted);
                            m_parserUpdater.Start(true, true);
                        } break;

                    case 100 + 3:
                        DBOption.SetOptions(DBOption.cView_Episode_OnlyShowLocalFiles, !DBOption.GetOptions(DBOption.cView_Episode_OnlyShowLocalFiles));
                        LoadFacade();
                        break;

                    case 100 + 4:
                        DBOption.SetOptions(DBOption.cView_Episode_HideUnwatchedSummary, !DBOption.GetOptions(DBOption.cView_Episode_HideUnwatchedSummary));
                        LoadFacade();
                        break;
                }

/*                GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
                if (dlg == null) return;
                dlg.Reset();
                dlg.SetHeading(924); // menu
                GUIListItem pItem = new GUIListItem("Download Coverart");
                dlg.Add(pItem);
                pItem = new GUIListItem("Refresh Information");
                dlg.Add(pItem);
                pItem = new GUIListItem("Play All");
                dlg.Add(pItem);
                pItem = new GUIListItem("Import new Videos");
                dlg.Add(pItem);
                dlg.DoModal(GetID);
                if (dlg.SelectedId == -1) return;

                GUIDialogOK pDlgOK = (GUIDialogOK)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_OK);
                pDlgOK.SetHeading("Feature not Implemented");

                switch (dlg.SelectedId)
                {
                    case 1:
                        pDlgOK.SetLine(1, "This feature will allow you to download");
                        pDlgOK.SetLine(2, "images from Amazon and Google.");
                        pDlgOK.DoModal(GUIWindowManager.ActiveWindow);

                        break;

                    case 2:
                        pDlgOK.SetLine(1, "This feature will allow you to update");
                        pDlgOK.SetLine(2, "the information from tv.com.");
                        pDlgOK.DoModal(GUIWindowManager.ActiveWindow);
                        break;

                    case 3:
                        #region Play All
                        PlayListPlayer playlistPlayer = new PlayListPlayer();
                        playlistPlayer.Reset();
                        playlistPlayer.CurrentPlaylistType = PlayListType.PLAYLIST_VIDEO_TEMP;
                        PlayList playlist = playlistPlayer.GetPlaylist(PlayListType.PLAYLIST_VIDEO_TEMP);
                        playlist.Clear();
                        if (this.m_ListLevel == cListLevelSeries)
                        {
                            DBSeries series = (DBSeries)currentitem.TVTag;
                            foreach (DBEpisode episode in DBEpisode.Get(series[DBSeries.cParsedName], true))
                            {
                                PlayListItem itemNew = new PlayListItem();
                                itemNew.FileName = episode[DBEpisode.cFilename];
                                itemNew.Description = episode[DBOnlineEpisode.cEpisodeSummary];  // not working
                                itemNew.Type = Playlists.PlayListItem.PlayListItemType.Video;
                                itemNew.MusicTag = episode;
                                playlist.Add(itemNew);
                            }
                        }
                        else if (this.m_ListLevel == cListLevelSeasons)
                        {
                            DBSeason season = (DBSeason)currentitem.TVTag;
                            foreach (DBEpisode episode in DBEpisode.Get(this.m_SelectedSeries[DBSeries.cParsedName], season[DBSeason.cIndex], true))
                            {
                                PlayListItem itemNew = new PlayListItem();
                                itemNew.FileName = episode[DBEpisode.cFilename];
                                itemNew.Description = episode[DBOnlineEpisode.cEpisodeSummary];  // not working
                                itemNew.Type = Playlists.PlayListItem.PlayListItemType.Video;
                                itemNew.MusicTag = episode;
                                playlist.Add(itemNew);
                            }
                        }
                        else if (this.m_ListLevel == cListLevelEpisodes)
                        {
                            DBEpisode episode = (DBEpisode)currentitem.TVTag;
                            PlayListItem itemNew = new PlayListItem();
                            itemNew.FileName = episode[DBEpisode.cFilename];
                            GUIPropertyManager.SetProperty("#plot", episode[DBOnlineEpisode.cEpisodeSummary]);
                            itemNew.Description = episode[DBOnlineEpisode.cEpisodeSummary];  // not working
                            itemNew.Type = Playlists.PlayListItem.PlayListItemType.Video;
                            itemNew.MusicTag = episode;
                            playlist.Add(itemNew);
                        }

                        playlistPlayer.PlayNext();

                        DBEpisode currentPlayingEpisode = (DBEpisode)playlistPlayer.GetCurrentItem().MusicTag;
                        DBSeries currentPlayingSeries = new DBSeries(currentPlayingEpisode[DBEpisode.cSeriesParsedName]);
                        
                        this.m_Logs.Write("Playing Movie: " + currentPlayingSeries[DBSeries.cPrettyName] + " - " + currentPlayingEpisode[DBEpisode.cSeasonIndex] + "x" + currentPlayingEpisode[DBEpisode.cEpisodeIndex] + " - " + currentPlayingEpisode[DBEpisode.cEpisodeName]);
                        
                        GUIPropertyManager.SetProperty("#title", currentPlayingSeries[DBSeries.cPrettyName] + " - " + currentPlayingEpisode[DBEpisode.cSeasonIndex] + "x" + currentPlayingEpisode[DBEpisode.cEpisodeIndex] + " - " + currentPlayingEpisode[DBEpisode.cEpisodeName]);
                        GUIPropertyManager.SetProperty("#genre", currentPlayingSeries[DBSeries.cGenre]);
                        GUIPropertyManager.SetProperty("#file", currentPlayingEpisode[DBEpisode.cFilename]);
//                        GUIPropertyManager.SetProperty("#plot", currentPlayingEpisode.Description);

                        //GUIPropertyManager.SetProperty("#year", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#director", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#cast", currentPlayingEpisode);	
                        //GUIPropertyManager.SetProperty("#dvdlabel", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#imdbnumber", currentPlayingEpisode);
                        GUIPropertyManager.SetProperty("#plotoutline", currentPlayingEpisode[DBOnlineEpisode.cEpisodeSummary]);
                        //GUIPropertyManager.SetProperty("#rating", currentPlayingEpisode);	
                        //GUIPropertyManager.SetProperty("#tagline", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#votes", currentPlayingEpisode);	
                        //GUIPropertyManager.SetProperty("#credits", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#mpaarating", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#runtime", currentPlayingEpisode);   
                        //GUIPropertyManager.SetProperty("#iswatched", currentPlayingEpisode); 

//                         GUIPropertyManager.SetProperty("#Play.Current.Thumb", currentPlayingSeries.GetImage());
                        GUIPropertyManager.SetProperty("#Play.Current.File", currentPlayingEpisode[DBEpisode.cFilename]);
                        GUIPropertyManager.SetProperty("#Play.Current.Title", currentPlayingSeries[DBSeries.cPrettyName] + " - " + currentPlayingEpisode[DBEpisode.cSeasonIndex] + "x" + currentPlayingEpisode[DBEpisode.cEpisodeIndex] + " - " + currentPlayingEpisode[DBEpisode.cEpisodeName]);
                        GUIPropertyManager.SetProperty("#Play.Current.Genre", currentPlayingSeries[DBSeries.cGenre]);
                        //GUIPropertyManager.SetProperty("#Play.Current.Comment", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Artist", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Director", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Album", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Track", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Year", currentPlayingEpisode);
//                        GUIPropertyManager.SetProperty("#Play.Current.Duration", currentPlayingSeries.Duration);
                        GUIPropertyManager.SetProperty("#Play.Current.Plot", currentPlayingEpisode[DBOnlineEpisode.cEpisodeSummary]);
                        //GUIPropertyManager.SetProperty("#Play.Current.PlotOutline", currentPlayingEpisode.Description);
                        //GUIPropertyManager.SetProperty("#Play.Current.Channel", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Cast", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.DVDLabel", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.IMDBNumber", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Rating", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.TagLine", currentPlayingEpisode.Description);
                        //GUIPropertyManager.SetProperty("#Play.Current.Votes", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Credits", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.Runtime", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.MPAARating", currentPlayingEpisode);
                        //GUIPropertyManager.SetProperty("#Play.Current.IsWatched", currentPlayingEpisode);

                        #endregion
                        break;
                    case 4:
                        GUIDialogProgress pDlgProgress = (GUIDialogProgress)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_PROGRESS);
                        pDlgProgress.SetHeading("Importing Videos");
                        pDlgProgress.SetLine(1, "Scanning folder for video files...");
                        pDlgProgress.StartModal(GUIWindowManager.ActiveWindow);
                        pDlgProgress.ShowProgressBar(true);
                        pDlgProgress.Progress();
// 
//                         ImportVideo import = new ImportVideo(this.m_Database);
//                         pDlgProgress.SetLine(2, "Found " + import.GetFiles.Length.ToString() + " video files.");
//                         import.SetGUIProperties(ref pDlgProgress);
//                         pDlgProgress.SetLine(1, "Importing new videos...");
//                         pDlgProgress.SetLine(2, "");
//                         pDlgProgress.Progress();
//                         System.Threading.Thread importThread = new System.Threading.Thread(new System.Threading.ThreadStart(import.Start));
//                         importThread.Start();
                        this.LoadFacade();
                        break;
                }
 */
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("The 'OnShowContextMenu' function has generated an error: " + ex.Message);
            }

        }

        public override void OnAction(Action action)
        {
            switch (action.wID)
            {
                case Action.ActionType.ACTION_PARENT_DIR:
                case Action.ActionType.ACTION_HOME:
                case Action.ActionType.ACTION_PREVIOUS_MENU:
                    // simulate a back
                    OnClicked(this.m_Button_Back.GetID, this.m_Button_Back, action.wID);
                    break;

                default:
                    base.OnAction(action);
                    break;
            }
        }

        protected override void OnClicked(int controlId, GUIControl control, MediaPortal.GUI.Library.Action.ActionType actionType)
        {
            if (control == this.m_Button_Back)
            {
                switch (this.m_ListLevel)
                {
                    case cListLevelSeries:
                        GUIWindowManager.ShowPreviousWindow();
                        break;
                    case cListLevelSeasons:
                        this.m_ListLevel = cListLevelSeries;
                        this.m_SelectedSeason = null;
                        break;
                    case cListLevelEpisodes:
                        this.m_ListLevel = cListLevelSeasons;
                        this.m_SelectedEpisode = null;
                        break;
                }
                this.LoadFacade();
            }
            else if (control == this.m_Button_View)
            {
                switch (this.m_Facade.View)
                {
                    case GUIFacadeControl.ViewMode.SmallIcons:
                        this.m_Facade.View = GUIFacadeControl.ViewMode.List;
                        GUIControl.SetControlLabel(GetID, controlId, GUILocalizeStrings.Get(101));
                        break;
                    case GUIFacadeControl.ViewMode.List:
                        this.m_Facade.View = GUIFacadeControl.ViewMode.LargeIcons;
                        GUIControl.SetControlLabel(GetID, controlId, GUILocalizeStrings.Get(417));
                        break;
                    case GUIFacadeControl.ViewMode.LargeIcons:
                        this.m_Facade.View = GUIFacadeControl.ViewMode.SmallIcons;
                        GUIControl.SetControlLabel(GetID, controlId, GUILocalizeStrings.Get(100));
                        break;
                }
                this.LoadFacade();
            }
            else if (control == this.m_Facade)
            {
                if (this.m_Facade.SelectedListItem.TVTag == null)
                    return;

                switch (this.m_ListLevel)
                {
                    case cListLevelSeries:
                        this.m_ListLevel = cListLevelSeasons;
                        this.m_SelectedSeries = (DBSeries)this.m_Facade.SelectedListItem.TVTag;
                        this.LoadFacade();
                        this.m_Facade.Focus = true;
                        break;
                    case cListLevelSeasons:
                        this.m_ListLevel = cListLevelEpisodes;
                        this.m_SelectedSeason = (DBSeason)this.m_Facade.SelectedListItem.TVTag;
                        this.LoadFacade();
                        this.m_Facade.Focus = true;
                        break;
                    case cListLevelEpisodes:
                        this.m_SelectedEpisode = (DBEpisode)this.m_Facade.SelectedListItem.TVTag;
                        this.m_SelectedEpisode[DBEpisode.cWatched] = 1;
                        this.m_SelectedEpisode.Commit();
                        this.LoadFacade();
                       
                        m_VideoHandler.ResumeOrPlay(m_SelectedEpisode[DBEpisode.cFilename]);
                        
//                         GUIDialogOK pDlgOK = (GUIDialogOK)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_OK);
//                         pDlgOK.SetHeading("Could not launch video in player");
//                         if (g_Player.Play(m_SelectedEpisode[DBEpisode.cFilename]))
//                         {
//                             if (Utils.IsVideo(m_SelectedEpisode[DBEpisode.cFilename]) && g_Player.HasVideo)
//                             {
//                                 GUIGraphicsContext.IsFullScreenVideo = true;
//                                 GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_FULLSCREEN_VIDEO);
//                                 GUIPropertyManager.SetProperty("#title", m_SelectedSeries[DBSeries.cPrettyName] + " - " + m_SelectedEpisode[DBEpisode.cSeasonIndex] + "x" + m_SelectedEpisode[DBEpisode.cEpisodeIndex] + " - " + m_SelectedEpisode[DBEpisode.cEpisodeName]);
//                                 GUIPropertyManager.SetProperty("#genre", m_SelectedSeries[DBSeries.cGenre]);
//                                 GUIPropertyManager.SetProperty("#file", m_SelectedEpisode[DBEpisode.cFilename]);
//                                 GUIPropertyManager.SetProperty("#plot", m_SelectedEpisode[DBOnlineEpisode.cEpisodeSummary]);
//                             }
//                             else
//                             {
//                                 pDlgOK.SetLine(1, "File format not recognized.  Ensure that the");
//                                 pDlgOK.SetLine(2, "proper codecs are installed on this system.");
//                                 pDlgOK.DoModal(GUIWindowManager.ActiveWindow);
//                             }
//                         }
//                         else
//                         {
//                             if (!System.IO.File.Exists(m_SelectedEpisode[DBEpisode.cFilename]))
//                             {
//                                 pDlgOK.SetLine(1, "Could not locate the file in the location");
//                                 pDlgOK.SetLine(2, "specified in the plugin's configuration.");
//                             }
//                             else
//                             {
//                                 pDlgOK.SetLine(1, "File format not recognized.  Ensure that the");
//                                 pDlgOK.SetLine(2, "proper codecs are installed on this system.");
//                             }
//                             pDlgOK.DoModal(GUIWindowManager.ActiveWindow);
//                         }
                        

                        break;
                }
            }
            base.OnClicked(controlId, control, actionType);
        }

        public void Clock(Object stateInfo)
        {
            if (m_parserUpdater == null)
            {
                // need to not be doing something yet (we don't want to accumulate parser objects !)
                bool bLocalScanNeeded = false;
                bool bUpdateScanNeeded = false;
                if (m_nLocalScanLapse > 0)
                {
                    TimeSpan tsLocal = DateTime.Now - m_LastLocalScan;
                    if ((int)tsLocal.TotalMinutes > m_nLocalScanLapse)
                        bLocalScanNeeded = true;
                }

                if (m_nUpdateScanLapse > 0)
                {
                    TimeSpan tsUpdate = DateTime.Now - m_LastUpdateScan;
                    if ((int)tsUpdate.TotalHours > m_nUpdateScanLapse)
                        bUpdateScanNeeded = true;
                }

                if (bLocalScanNeeded || bUpdateScanNeeded)
                {
                    // only load the wait cursor if we are in the plugin
                    if (m_Facade != null)
                        m_waitCursor = new WaitCursor();

                    // do scan
                    m_parserUpdater = new OnlineParsing();
                    m_parserUpdater.OnlineParsingCompleted += new OnlineParsing.OnlineParsingCompletedHandler(parserUpdater_OnlineParsingCompleted);
                    m_parserUpdater.Start(bLocalScanNeeded, bUpdateScanNeeded);
                }
            }
            base.Process();
        }

        void parserUpdater_OnlineParsingCompleted(bool bDataUpdated)
        {
            if (m_waitCursor != null)
            {
                m_waitCursor.Dispose();
                m_waitCursor = null;
            }
            if (m_parserUpdater != null)
            {
                if (m_parserUpdater.LocalScan)
                {
                    m_LastLocalScan = DateTime.Now;
                    DBOption.SetOptions(DBOption.cLocalScanLastTime, m_LastLocalScan.ToString());
                }
                if (m_parserUpdater.UpdateScan)
                {
                    m_LastUpdateScan = DateTime.Now;
                    DBOption.SetOptions(DBOption.cUpdateScanLastTime, m_LastUpdateScan.ToString());
                }
                m_parserUpdater = null;
                if (bDataUpdated)
                    LoadFacade();
            }
        }

        public override bool OnMessage(GUIMessage message)
        {
            switch (message.Message)
            {
                case GUIMessage.MessageType.GUI_MSG_ITEM_FOCUS_CHANGED:
                    {
                        int iControl = message.SenderControlId;
                        if (iControl == (int)m_Facade.GetID)
                        {
                            switch (this.m_ListLevel)
                            {
                                case cListLevelSeries:
                                    Series_OnItemSelected(m_Facade.SelectedListItem);
                                    break;

                                case cListLevelSeasons:
                                    Season_OnItemSelected(m_Facade.SelectedListItem);
                                    break;

                                case cListLevelEpisodes:
                                    Episode_OnItemSelected(m_Facade.SelectedListItem);
                                    break;
                            }
                        }
                    }
                    break;
            }
            return base.OnMessage(message);
        }

        private int CountCRLF(String sIn)
        {
            int nCount = -1;
            int nNext = 0;
            do
            {
                nCount++;
                nNext = sIn.IndexOf((char)10, nNext+1);
            }
            while (nNext != -1);
            return nCount;
        }

        private void Series_OnItemSelected(GUIListItem item)
        {
            if (item == null)
                return;

            DBSeries series = (DBSeries)item.TVTag;
            m_SelectedSeries = series;
            if (this.m_Image != null)
            {
                String filename = series.Banner;
                if (filename != null)
                {
                    // SetFileName needs to be tried/catched, as it can fail because we are on another thread. This sucks, but IMHO it's a design problem 
                    // of Mediaportal: all events should be sent on the main thread.
                    try
                    {
                        this.m_Image.SetFileName(filename);
                        this.m_Image.KeepAspectRatio = true;
                    }
                    catch {}
                }
            }
            if (m_Season_Image != null)
            {
                try
                {
                    m_Season_Image.FreeResources();
                }
                catch { }
            }

            int nStartOffset = m_Image.YPosition + m_Image.Height + 5;
            int nBottomLimit = m_Description.YPosition + m_Description.Height;
            if (m_Title != null)
            {
                m_Title.YPosition = nStartOffset;
                m_Title.Label = FormatField(m_sFormatSeriesTitle, series);
                nStartOffset += m_Title.Height + 5;
            }

            if (m_Genre != null)
            {
                m_Genre.YPosition = nStartOffset;
                String sLabel = FormatField(m_sFormatSeriesSubtitle, series);
                m_Genre.Label = sLabel;
                int nLines = CountCRLF(sLabel) + 1;
                if (nLines > 4)
                    nLines = 4;
                m_Genre.Height = m_Genre.ItemHeight * (nLines);
                nStartOffset += m_Genre.Height + 5;
            }

            if (this.m_Description != null)
            {
                m_Description.YPosition = nStartOffset;
                m_Description.Height = nBottomLimit - nStartOffset;
                m_Description.Label = FormatField(m_sFormatSeriesMain, series);
            }
        }

        private void Season_OnItemSelected(GUIListItem item)
        {
            if (item == null)
                return;

            DBSeason season = (DBSeason)item.TVTag;
            if (this.m_Season_Image != null)
            {
                String filename = season.Banner;
                if (filename != null)
                {
                    // SetFileName needs to be tried/catched, as it can fail because we are on another thread. This sucks, but IMHO it's a design problem 
                    // of Mediaportal: all events should be sent on the main thread.
                    try
                    {
                        this.m_Season_Image.SetFileName(filename);
                    }
                    catch { }
                }
            }
            if (this.m_Title != null)
                GUIControl.SetControlLabel(GetID, m_Title.GetID, this.m_SelectedSeries[DBSeries.cPrettyName] + " Season " + season[DBSeason.cIndex]);
            if (this.m_Genre != null)
                GUIControl.SetControlLabel(GetID, m_Genre.GetID, m_SelectedSeries[DBSeries.cGenre]);
            if (this.m_Description != null)
                GUIControl.SetControlLabel(GetID, m_Description.GetID, (String)this.m_SelectedSeries[DBSeries.cSummary] + (char)10 + (char)13);
        }
        private void Episode_OnItemSelected(GUIListItem item)
        {
            if (item == null)
                return;

            DBEpisode episode = (DBEpisode)item.TVTag;

            int nStartOffset = m_Image.YPosition + m_Image.Height + 5;
            int nBottomLimit = m_Description.YPosition + m_Description.Height;
            if (m_Title != null)
            {
                m_Title.YPosition = nStartOffset;
                m_Title.Label = FormatField(m_sFormatEpisodeTitle, episode);
                nStartOffset += m_Title.Height + 5;
            }

            if (m_Genre != null)
            {
                m_Genre.YPosition = nStartOffset;
                String sLabel = FormatField(m_sFormatEpisodeSubtitle, episode);
                m_Genre.Label = sLabel;
                int nLines = CountCRLF(sLabel) + 1;
                if (nLines > 4)
                    nLines = 4;
                m_Genre.Height = m_Genre.ItemHeight * (nLines);
                nStartOffset += m_Genre.Height + 5;
            }

            if (this.m_Description != null)
            {
                m_Description.YPosition = nStartOffset;
                m_Description.Height = nBottomLimit - nStartOffset;
                m_Description.Label = FormatField(m_sFormatEpisodeMain, episode);
            }
        }
    }
}