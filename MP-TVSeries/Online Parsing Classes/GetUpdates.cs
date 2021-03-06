using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Linq;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes
{
    class GetUpdates
    {
        long timestamp;
        Dictionary<DBValue, long> series = null;
        Dictionary<DBValue, long> episodes = null;
        Dictionary<DBValue, long> banners = null;
        Dictionary<DBValue, long> fanart = null;

        public long OnlineTimeStamp
        { get { return timestamp; } }

        public Dictionary<DBValue, long> UpdatedSeries
        { get { return series; } }

        public Dictionary<DBValue, long> UpdatedEpisodes
        { get { return episodes; } }

        public Dictionary<DBValue, long> UpdatedBanners
        { get { return banners; } }

        public Dictionary<DBValue, long> UpdatedFanart
        { get { return fanart; } }

        public GetUpdates(OnlineAPI.UpdateType type)
        {
            if (type != OnlineAPI.UpdateType.all)
                MPTVSeriesLog.Write(string.Format("Downloading updates from the last {0}", type.ToString()));
            else
                MPTVSeriesLog.Write("Downloading all updates");

            XmlNode updates = OnlineAPI.Updates(type);

            series = new Dictionary<DBValue, long>();
            episodes = new Dictionary<DBValue, long>();
            banners = new Dictionary<DBValue, long>();
            fanart = new Dictionary<DBValue, long>();

            // if updates via zip fails, try xml
            if ( updates == null )
            {
                MPTVSeriesLog.Write( "Failed to get updates from 'zip' file, trying 'xml'...");
                updates = OnlineAPI.Updates( type, OnlineAPI.Format.Xml );

                // if we're still failing to get updates...
                if ( updates == null )
                {
                    // manually define what series need updating basis whether the series is continuing and has local episodes
                    SQLCondition condition = new SQLCondition();
                    condition.Add( new DBOnlineSeries(), DBOnlineSeries.cID, 0, SQLConditionType.GreaterThan );
                    condition.Add( new DBOnlineSeries(), DBOnlineSeries.cHasLocalFiles, 1, SQLConditionType.Equal );
                    condition.Add( new DBOnlineSeries(), DBOnlineSeries.cStatus, "Ended", SQLConditionType.NotEqual );
                    condition.Add( new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal );
                    condition.Add( new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal );

                    var lContinuingSeries = DBSeries.Get( condition, false, false );
                    MPTVSeriesLog.Write( $"Failed to get updates from online, manually defining series and images for updates. Database contains '{lContinuingSeries.Count}' continuing series with local files" );
                    
                    // force our local download cache to expire after a 12hrs
                    timestamp = DateTime.UtcNow.Subtract( new TimeSpan( 0, 12, 0, 0 ) ).ToEpoch();
                    foreach ( var lSeries in lContinuingSeries )
                    {
                        string lSeriesId = lSeries[DBOnlineSeries.cID];

                        series.Add( lSeriesId, timestamp );
                        banners.Add( lSeriesId, timestamp );
                        fanart.Add( lSeriesId, timestamp );

                        // get the most recent season as that is the one that is most likely recently updated
                        // NB: specials could also be recently updated
                        var lSeasons = DBSeason.Get( int.Parse( lSeriesId ) );
                        if ( lSeasons != null && lSeasons.Count > 0 )
                        {
                            int lSeasonIndex = lSeasons.Max( s => ( int )s[DBSeason.cIndex] );

                            var lEpisodes = DBEpisode.Get( int.Parse( lSeriesId ), lSeasonIndex );
                            lEpisodes.AddRange( DBEpisode.Get( int.Parse( lSeriesId ), 0 ) );

                            foreach ( var episode in lEpisodes )
                            {
                                episodes.Add( episode[DBOnlineEpisode.cID], timestamp );
                            }
                        }
                    }
                }
                else
                {
                    long.TryParse( updates.Attributes["time"].Value, out timestamp );

                    // NB: updates from xml only includes series (no episodes or artwork!)
                    foreach ( XmlNode node in updates.SelectNodes( "/Data/Series" ) )
                    {
                        long lTime;
                        long.TryParse( node.SelectSingleNode( "time" ).InnerText, out lTime );
                        string lSeriesId = node.SelectSingleNode( "id" ).InnerText;

                        series.Add( lSeriesId, lTime );
                        banners.Add( lSeriesId, lTime );
                        fanart.Add( lSeriesId, lTime );

                        // get the most recent season as that is the one that is most likely recently updated
                        // NB: specials could also be recently updated
                        if ( Helper.getCorrespondingSeries( int.Parse( lSeriesId ) ) != null )
                        {
                            var lSeasons = DBSeason.Get( int.Parse( lSeriesId ) );
                            if ( lSeasons != null && lSeasons.Count > 0 )
                            {
                                int lSeasonIndex = lSeasons.Max( s => ( int )s[DBSeason.cIndex] );

                                var lEpisodes = DBEpisode.Get( int.Parse( lSeriesId ), lSeasonIndex );
                                lEpisodes.AddRange( DBEpisode.Get( int.Parse( lSeriesId ), 0 ) );

                                foreach ( var episode in lEpisodes )
                                {
                                    episodes.Add( episode[DBOnlineEpisode.cID], lTime );
                                }
                            }
                        }
                    }
                }
                return;
            }

            // process zip file update...
            long.TryParse(updates.Attributes["time"].Value, out this.timestamp);

            // get all the series ids
            foreach (XmlNode node in updates.SelectNodes("/Data/Series"))
            {
                long time;
                long.TryParse(node.SelectSingleNode("time").InnerText, out time);
                this.series.Add(node.SelectSingleNode("id").InnerText, time);
            }

            // get all the episode ids
            foreach (XmlNode node in updates.SelectNodes("/Data/Episode"))
            {
                long time;
                long.TryParse(node.SelectSingleNode("time").InnerText, out time);
                this.episodes.Add(node.SelectSingleNode("id").InnerText, time);
            }
            
            // get all the season banners
            string id = string.Empty;
            long value;

            foreach (XmlNode node in updates.SelectNodes("/Data/Banner[type='season']"))
            {
                long time;
                long.TryParse(node.SelectSingleNode("time").InnerText, out time);
                id = node.SelectSingleNode("Series").InnerText;
                if (!this.banners.TryGetValue(id, out value))
                    this.banners.Add(id, time);
            }

            //get all the series banners
            foreach (XmlNode node in updates.SelectNodes("/Data/Banner[type='series']"))
            {
                long time;
                long.TryParse(node.SelectSingleNode("time").InnerText, out time);
                id = node.SelectSingleNode("Series").InnerText;
                if (!this.banners.TryGetValue(id, out value))
                    this.banners.Add(id, time);
            }

            //get all the poster banners
            foreach (XmlNode node in updates.SelectNodes("/Data/Banner[type='poster']"))
            {
                long time;
                long.TryParse(node.SelectSingleNode("time").InnerText, out time);
                id = node.SelectSingleNode("Series").InnerText;
                if (!this.banners.TryGetValue(id, out value))
                    this.banners.Add(id, time);
            }

            //get all the fanart banners
            id = string.Empty;
            foreach (XmlNode node in updates.SelectNodes("/Data/Banner[type='fanart']"))
            {
                long time;
                long.TryParse(node.SelectSingleNode("time").InnerText, out time);
                id = node.SelectSingleNode("Series").InnerText;
                if (!this.fanart.TryGetValue(id, out value))
                    this.fanart.Add(id, time);
            }

        }
    }
}
