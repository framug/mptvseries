using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowPlugins.GUITVSeries
{
    sealed class FieldGetter
    {
        const string Episode = "Episode";
        const string Season = "Season";
        const string Series = "Series";
        const char openTag = '<';
        const char closeTag = '>';
        const char typeFieldSeperator = '.';

        static string generalRegex = @"(?<=\" + openTag + @"{0}\.)(?<fieldtoParse>.*?)(?=\" + closeTag + ")";

        static Regex epParse = new Regex(String.Format(generalRegex, Episode), RegexOptions.Compiled | RegexOptions.Singleline);
        static Regex seasonParse = new Regex(String.Format(generalRegex, Season), RegexOptions.Compiled | RegexOptions.Singleline);
        static Regex seriesParse = new Regex(String.Format(generalRegex, Series), RegexOptions.Compiled | RegexOptions.Singleline);

        static string epIdentifier = String.Format("{0}{1}{2}", openTag, Episode, typeFieldSeperator);
        static string seasonIdentifier = String.Format("{0}{1}{2}", openTag, Season, typeFieldSeperator);
        static string seriesIdentifier = String.Format("{0}{1}{2}", openTag, Series, typeFieldSeperator);

        static bool _splitFields = true; // not thread safe

        private FieldGetter() { }
        public enum Level
        {
            Series,
            Season,
            Episode,
        }

        static List<Level> getLevel(string what)
        {
            List<Level> levels = new List<Level>();
            if (what.Contains(epIdentifier)) levels.Add(Level.Episode);
            if (what.Contains(seasonIdentifier)) levels.Add(Level.Season);
            if (what.Contains(seriesIdentifier)) levels.Add(Level.Series);
            return levels;
        }

        static Level levelOfItem(DBTable item)
        {
            Type p = item.GetType();
            if (p == typeof(DBSeries)) return Level.Series;
            if (p == typeof(DBSeason)) return Level.Season;
            if (p == typeof(DBEpisode)) return Level.Episode;
            return Level.Series;
        }

        public static string resolveDynString(string what, DBTable item)
        {
            return resolveDynString(what, item, true);
        }
        public static string resolveDynString(string what, DBTable item, bool splitFields)
        {
            perfana.Start();
            Level level = levelOfItem(item);
            string value = what;
            List<Level> whatLevels = getLevel(what);
            _splitFields = splitFields;
            // the item needs to be the type corresponding to the level (we require the item to match the indicated level)
            if (level == Level.Episode) // we can do everything
            {
                if (whatLevels.Contains(Level.Episode))
                    value = replaceEpisodeTags(item as DBEpisode, value);
                if (whatLevels.Contains(Level.Season))
                    value = replaceSeriesTags(item[DBEpisode.cSeriesID], value);
                if (whatLevels.Contains(Level.Series))
                    value = replaceSeriesTags(item[DBEpisode.cSeriesID], value);
            }
            else if (level == Level.Season && !whatLevels.Contains(Level.Episode)) // we can do season/series
            {
                if(whatLevels.Contains(Level.Season))
                    value = replaceSeasonTags(item as DBSeason, value);
                if (whatLevels.Contains(Level.Series))
                    value = replaceSeriesTags(item[DBSeason.cSeriesID], value);
            }
            else if (level == Level.Series && !whatLevels.Contains(Level.Episode) && !whatLevels.Contains(Level.Season)) // we can only do series
            {
                value = replaceSeriesTags(item as DBSeries, value);
            }
            value = MathParser.mathParser.TryParse(value);
            perfana.Stop();
            return value;
        }

        static string replaceSeriesTags(int seriesID, string what)
        {
            // get the series (tries cache first and then the db)
            return replaceSeriesTags(Helper.getCorrespondingSeries(seriesID), what);
        }
        static string replaceSeriesTags(DBSeries s, string what)
        {
            if (s == null || what.Length < seriesIdentifier.Length) return what;
            return getValuesOfType(s, what, seriesParse, seriesIdentifier);
        }

        static string replaceSeasonTags(int seriesID, int seasonIndex, string what)
        {
            // get the series (tries cache first and then the db)
            return replaceSeasonTags(Helper.getCorrespondingSeason(seriesID, seasonIndex), what);
        }
        static string replaceSeasonTags(DBSeason s, string what)
        {
            if (s == null || what.Length < seasonIdentifier.Length) return what;
            return getValuesOfType(s, what, seasonParse, seasonIdentifier);
        }

        static string replaceEpisodeTags(DBEpisode s, string what)
        {
            if (s == null || what.Length < epIdentifier.Length) return what;
            return getValuesOfType(s, what, epParse, epIdentifier);
        }

        static string getValuesOfType(DBTable item, string what, Regex matchRegex, string Identifier)
        {
            string value = what;
            foreach (Match m in matchRegex.Matches(what))
            {
                string result = item[m.Value];
                if (_splitFields) result = result.Trim('|').Replace("|", ", ");
                value = value.Replace(Identifier + m.Value + ">", result);
            }
            return value;
        }
    }
}
