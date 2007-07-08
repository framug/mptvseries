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
using System.Text;
using SQLite.NET;
using MediaPortal.Database;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.IO;

namespace WindowPlugins.GUITVSeries
{
    public class DBValue
    {
        private String value = String.Empty;

        public override String ToString()
        {
            return value;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public DBValue(String value)
        {
            if (value != null)
                this.value = value;
            else
                this.value = String.Empty;
        }
        public DBValue(Boolean value)
        {
            // booleans are 0/1
            this.value = Convert.ToInt16(value).ToString();
        }

        public DBValue(int value)
        {
            this.value = value.ToString();
        }
        public DBValue(long value)
        {
            this.value = value.ToString();
        }

        static public implicit operator String(DBValue value)
        {
            if (value == null)
                return "";

            return value.value;
        }

        static public implicit operator Boolean(DBValue value)
        {
            if (value == null)
                return false;

            if (value.value.Length > 0 && value.value != "0")
                return true;
            else
                return false;
        }
        
        static public implicit operator int(DBValue value)
        {
            if (null == value) return 0;
            try { return Convert.ToInt32(value.value); }
            catch (System.FormatException) { return 0; }
        }

        static public implicit operator long(DBValue value)
        {
            try { return Convert.ToInt64(value.value); }
            catch (System.FormatException) { return 0; }
        }

        static public implicit operator DBValue(String value)
        {
            return new DBValue(value);
        }

        static public implicit operator DBValue(Boolean value)
        {
            return new DBValue(value);
        }

        static public implicit operator DBValue(int value)
        {
            return new DBValue(value);
        }

        static public implicit operator DBValue(long value)
        {
            return new DBValue(value);
        }

        static public bool operator == (DBValue first, DBValue second)
        {
            if ((object)first == null || (object)second == null)
            {
                if ((object)first == null && (object)second == null)
                    return true;
                else
                    return false;
            }
            return first.value == second.value;
        }

        static public bool operator != (DBValue first, DBValue second)
        {
            if ((object)first == null || (object)second == null)
            {
                if ((object)first == null && (object)second == null)
                    return false;
                else
                    return true;
            } return first.value != second.value;
        }
    };

    // field class - used to hold information
    public class DBField
    {
        public enum cType
        {
            Int,
            String
        }

        // the following are remainders to easily change the type (because it was a string! comparision before)
        public const cType cTypeInt = cType.Int; 
        public const cType cTypeString = cType.String;

        // private access
        private cType m_type;
        private bool m_primaryKey;
        private DBValue m_value;

        public DBField(cType type)
        {
            m_type = type;
            m_primaryKey = false;
        }
        public DBField(cType type, bool primaryKey)
        {
            m_type = type;
            m_primaryKey = primaryKey;
        }

        public DBField(DBFieldType dbFieldT)
        {
            m_type = dbFieldT.Type;
            m_primaryKey = dbFieldT.Primary;
        }

        public bool Primary
        {
            get { return this.m_primaryKey; }
            set { this.m_primaryKey = value; }
        }

        public cType Type
        {
            get { return this.m_type; }
            set { this.m_type = value; }
        }

        public DBValue Value
        {
            // save DB friendly string (doubling singlequotes
            get { return this.m_value; }
            set { this.m_value = value; }
        }
    };

    public struct DBFieldType
    {
        public DBField.cType Type;
        public bool Primary;

    }

    // table class - used as a base for table objects (series, episodes, etc)
    // holds a field hash table, includes an update mechanism to keep the DB tables in sync
    public class DBTable
    {
        public string m_tableName;
        public Dictionary<string, DBField> m_fields;
        public bool m_CommitNeeded = false;
        public static List<string> FieldsRequiringSplit = new List<string>();
        public List<string> fieldsRequiringSplit = FieldsRequiringSplit;
        public delegate void dbUpdateOccuredDelegate(string table);
        public static event dbUpdateOccuredDelegate dbUpdateOccured;

        protected static Dictionary<string, Dictionary<string, DBFieldType>> fields = new Dictionary<string, Dictionary<string, DBFieldType>>();
        
        public DBTable(string tableName)
        {
            // this base constructor was very expensive
            // we now cache the result for all objects : dbtable and only redo it if an alter table occured (this drastically cut down the number of sql statements)
            // this piece of code alone took over 30% of the time on my machine when entering config and newing up all the episodes
            
            Dictionary<string, DBFieldType> cachedForTable;
            m_tableName = tableName;
            m_fields = new Dictionary<string, DBField>();
            if (fields.ContainsKey(tableName)) // good, cached, this happens 99% of the time
            {
                cachedForTable = fields[tableName];
                foreach (KeyValuePair<string, DBFieldType> entry in cachedForTable)
                    if (!m_fields.ContainsKey(entry.Key))
                        m_fields.Add(entry.Key, new DBField(entry.Value));
            }
            else // we have to get it, happens when the first object is created or after an alter table
            {
                cachedForTable = new Dictionary<string, DBFieldType>();
                // load up fields from the table
                SQLiteResultSet results;
                results = DBTVSeries.Execute("SELECT sql FROM sqlite_master WHERE name='" + m_tableName + "'");
                if (results != null && results.Rows.Count > 0)
                {
                    // we have the table definition, parse it for names/types
                    String sCreateTable = results.Rows[0].fields[0];
                    String RegExp = @"CREATE TABLE .*?\((.*?)\)";
                    Regex Engine = new Regex(RegExp, RegexOptions.IgnoreCase);
                    Match tablematch = Engine.Match(sCreateTable);
                    if (tablematch.Success)
                    {
                        String sParameters = tablematch.Groups[1].Value;
                        // we have the list of parameters, parse them
                        RegExp = @"([^\s,]+) ([^\s,]+)( primary key)?";
                        Engine = new Regex(RegExp, RegexOptions.IgnoreCase);
                        MatchCollection matches = Engine.Matches(sParameters);
                        foreach (Match parammatch in matches)
                        {
                            String sName = parammatch.Groups[1].Value;
                            String sType = parammatch.Groups[2].Value;
                            bool bPrimary = false;
                            if (parammatch.Groups[3].Success)
                                bPrimary = true;

                            DBFieldType cachedInfo = new DBFieldType();
                            cachedInfo.Primary = bPrimary;
                            cachedInfo.Type = (sType.ToLower() == "int" || sType.ToLower() == "integer") ? DBField.cTypeInt : DBField.cType.String;

                            if (!m_fields.ContainsKey(sName))
                            {
                                m_fields.Add(sName, new DBField(cachedInfo));
                            }

                            cachedForTable.Add(sName, cachedInfo);
                        }
                        lock(fields)
                            fields.Add(tableName, cachedForTable);
                    }
                    else
                    {
                        MPTVSeriesLog.Write("parsing of CREATE TABLE failed!!!");
                    }

                }
                else
                {
                    // no tables, assume it's going to be created later (using AddColumn)
                }
            }
        }

        public virtual bool AddColumn(String sName, DBField field)
        {
            // verify if we already have that field avail
            if (!m_fields.ContainsKey(sName))
            {
                if (m_fields.Count == 0 && !field.Primary) 
                {
                    throw new Exception("First field added needs to be the index");
                }

                try
                {
                    // ok, we don't, add it
                    SQLiteResultSet results;
                    results = DBTVSeries.Execute("SELECT name FROM sqlite_master WHERE name='" + m_tableName + "'");
                    if (results != null && results.Rows.Count > 0)
                    {
                        // table already exists, alter it
                        String sQuery = "ALTER TABLE " + m_tableName + " ADD " + sName + " " + field.Type;
                        DBTVSeries.Execute(sQuery);
                    }
                    else
                    {
                        // new table, create it
                        // no tables, assume it's going to be created later (using AddColumn)
                        String sQuery = "CREATE TABLE " + m_tableName + " (" + sName + " " + field.Type + (field.Primary ? " primary key)" : ")");
                        DBTVSeries.Execute(sQuery);
                    }
                    // delete the s_fields cache so newed up objects get the right fields
                    lock (fields)
                        fields.Remove(m_tableName);
                    m_fields.Add(sName, field);
                    return true;
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write(m_tableName + " table.AddColumn failed (" + ex.Message + ").");
                    return false;
                }
            }
            return false;
        }

        public virtual void InitValues()
        {
            foreach (KeyValuePair<string, DBField> fieldPair in m_fields)
            {
                if (!fieldPair.Value.Primary || fieldPair.Value.Value == null)
                {
                    switch (fieldPair.Value.Type)
                    {
                        case DBField.cTypeInt:
                            fieldPair.Value.Value = 0;
                            break;

                        case DBField.cTypeString:
                            fieldPair.Value.Value = "";
                            break;
                    }
                }
            }
            m_CommitNeeded = true;
        }

        public virtual DBValue this[String fieldName]
        {
            get 
            {
                if (m_fields.ContainsKey(fieldName))
                    return m_fields[fieldName].Value;
                else
                    return String.Empty;
            }
            set
            {
                try
                {
                    if (m_fields.ContainsKey(fieldName))
                    {
                        if (m_fields[fieldName].Type == DBField.cTypeInt)
                            m_fields[fieldName].Value = (long)value;
                        else
                            m_fields[fieldName].Value = value;
                        m_CommitNeeded = true;
                    }
                    else
                    {
                        AddColumn(fieldName, new DBField(DBField.cTypeString));
                        this[fieldName] = value;
                    }
                }
                catch (SystemException)
                {
                    MPTVSeriesLog.Write("Cast exception when trying to assign " + value + " to field " + fieldName + " in table " + m_tableName);
                }
            }
        }

        public virtual List<String> FieldNames
        {
            get
            {
                List<String> outList = new List<String>();
                foreach (KeyValuePair<string, DBField> pair in m_fields)
                {
                    outList.Add(pair.Key);
                }
                return outList;
            }
        }

        public static String Q(String sField)
        {
            return sField;
        }

        //public bool Read(ref SQLiteResultSet records, int index)
        //{
        //    if (records.Rows.Count > 0)
        //    {
        //        foreach (KeyValuePair<string, DBField> field in m_fields)
        //        {
        //            if (records.ColumnIndices.ContainsKey(field.Key))
        //                field.Value.Value = DatabaseUtility.Get(records, index, field.Key);
        //            else
        //                field.Value.Value = DatabaseUtility.Get(records, index, m_tableName + "." + field.Key);
        //        }
        //        m_CommitNeeded = false;
        //        return true;
        //    }
        //    return false;
        //}

        public bool Read(ref SQLiteResultSet records, int index)
        {
            if (records.Rows.Count > 0 || records.Rows.Count < index)
            {
                SQLiteResultSet.Row row = records.Rows[index];
                string res = null;
                int iCol = 0;
                foreach (KeyValuePair<string, DBField> field in m_fields)
                {
                    if (records.ColumnIndices.ContainsKey(field.Key))
                    {
                        iCol = (int)records.ColumnIndices[field.Key];
                        res = row.fields[iCol];
                        field.Value.Value = res == null ? string.Empty : res;
                    }
                    else // I don't get this? tablename.field ???
                        field.Value.Value = DatabaseUtility.Get(records, index, m_tableName + "." + field.Key);
                }
                m_CommitNeeded = false;
                return true;
            }
            return false;
        }

        public String PrimaryKey()
        {
            foreach (KeyValuePair<string, DBField> field in m_fields)
                if (field.Value.Primary == true)
                {
                    return field.Key;
                }

            return null;
        }

        public bool ReadPrimary(DBValue Value)
        {
            try
            {
                m_fields[PrimaryKey()].Value = Value;
                SQLCondition condition = new SQLCondition();
                condition.Add(this, PrimaryKey(), m_fields[PrimaryKey()].Value, SQLConditionType.Equal);
                String sqlQuery = "select * from " + m_tableName + condition;
                SQLiteResultSet records = DBTVSeries.Execute(sqlQuery);
                return Read(ref records, 0);
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("An Error Occurred (" + ex.Message + ").");
            }
            return false;
        }

        public virtual bool Commit()
        {
            try
            {
                if (!m_CommitNeeded)
                    return false;

                KeyValuePair<string, DBField> PrimaryField = new KeyValuePair<string, DBField>();

                foreach (KeyValuePair<string, DBField> field in m_fields)
                    if (field.Value.Primary == true)
                    {
                        PrimaryField = field;
                        break;
                    }

                if (Helper.String.IsNullOrEmpty(PrimaryField.Value.Value))
                    return false;

                String sWhere = " where ";
                switch (PrimaryField.Value.Type)
                {
                    case DBField.cTypeInt:
                        sWhere += PrimaryField.Key + " = " + PrimaryField.Value.Value;
                        break;

                    case DBField.cTypeString:
                        sWhere += PrimaryField.Key + " = '" + ((String)PrimaryField.Value.Value).Replace("'", "''") + "'";
                        break;
                }

                // use the primary key field
                String sqlQuery = "select " + PrimaryField.Key + " from " + m_tableName + sWhere;
                SQLiteResultSet records = DBTVSeries.Execute(sqlQuery);
                if (records.Rows.Count > 0)
                {
                    // already exists, update
                    sqlQuery = "update " + m_tableName + " set ";
                    foreach (KeyValuePair<string, DBField> fieldPair in m_fields)
                    {
                        if (!fieldPair.Value.Primary)
                        {
                            switch (fieldPair.Value.Type)
                            {
                                case DBField.cTypeInt:
                                    sqlQuery += fieldPair.Key + " = " + (Helper.String.IsNullOrEmpty(fieldPair.Value.Value) ? "''" : fieldPair.Value.Value + ",");
                                    break;

                                case DBField.cTypeString:
                                    sqlQuery += fieldPair.Key + " = '" + ((String)(fieldPair.Value.Value)).Replace("'", "''") + "',";
                                    break;
                            }
                            
                        }
                    }

                    sqlQuery = sqlQuery.Substring(0, sqlQuery.Length - 1) + sWhere;
                    DBTVSeries.Execute(sqlQuery);
                }
                else
                {
                    // add new record
                    String sParamNames = String.Empty;
                    String sParamValues = String.Empty;
                    foreach (KeyValuePair<string, DBField> fieldPair in m_fields)
                    {
                        sParamNames += fieldPair.Key + ",";
                        switch (fieldPair.Value.Type)
                        {
                            case DBField.cTypeInt:
                                sParamValues += fieldPair.Value.Value + ",";
                                break;

                            case DBField.cTypeString:
                                sParamValues += "'" + ((String)(fieldPair.Value.Value)).Replace("'", "''") + "',";
                                break;
                        }
                    }
                    sParamNames = sParamNames.Substring(0, sParamNames.Length - 1);
                    sParamValues = sParamValues.Substring(0, sParamValues.Length - 1);
                    sqlQuery = "insert into " + m_tableName + " (" + sParamNames + ") values(" + sParamValues + ")";
                    DBTVSeries.Execute(sqlQuery);
                }
                return true;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("An Error Occurred (" + ex.Message + ").");
                return false;
            }
        }

        public static void GlobalSet(DBTable obj, String sKey, DBValue Value, SQLCondition conditions)
        {
            if (obj.m_fields.ContainsKey(sKey))
            {
                String sqlQuery = "update " + obj.m_tableName + " SET " + sKey + "=";
                switch (obj.m_fields[sKey].Type)
                {
                    case DBField.cTypeInt:
                        sqlQuery += Value;
                        break;

                    case DBField.cTypeString:
                        sqlQuery += "'" + Value + "'";
                        break;
                }

                sqlQuery += conditions;
                SQLiteResultSet results = DBTVSeries.Execute(sqlQuery);
                if (dbUpdateOccured != null)
                    dbUpdateOccured(obj.m_tableName);
            }
        }

        public static void GlobalSet(DBTable obj, String sKey1, String sKey2, SQLCondition conditions)
        {
            if (obj.m_fields.ContainsKey(sKey1) && obj.m_fields.ContainsKey(sKey2))
            {
                String sqlQuery = "update " + obj.m_tableName + " SET " + sKey1 + " = " + sKey2 + conditions;
                SQLiteResultSet results = DBTVSeries.Execute(sqlQuery);
                if (dbUpdateOccured != null)
                    dbUpdateOccured(obj.m_tableName);
            }
        }

        public static void Clear(DBTable obj, SQLCondition conditions)
        {
            String sqlQuery = "delete from " + obj.m_tableName + conditions;
            SQLiteResultSet results = DBTVSeries.Execute(sqlQuery);
            if (dbUpdateOccured != null)
                dbUpdateOccured(obj.m_tableName);
        }

        protected static string getRandomBanner(List<string> BannerList)
        {
            const string graphicalBannerRecognizerSubstring = "-g";
            string langIdentifier = "-lang" + ZsoriParser.SelLanguageAsString + "-";

            // random banners are prefered in the following order
            // 1) own lang + graphical
            // 2) own lang but not graphical
            // 3) english + graphical (english really is any other language banners that are in db)
            // 4) english but not graphical

            string randImage = null;
            if (BannerList == null || BannerList.Count == 0) return string.Empty;
            if (BannerList.Count == 1) randImage = BannerList[0];

            if (randImage == null)
            {
                List<string> langG = new List<string>();
                List<string> lang = new List<string>();
                List<string> engG = new List<string>();
                List<string> eng = new List<string>();
                for (int i = 0; i < BannerList.Count; i++)
                {
                    if(File.Exists(BannerList[i]))
                    {
                        if(BannerList[i].Contains(graphicalBannerRecognizerSubstring))
                        {
                            if(BannerList[i].Contains(langIdentifier))
                                langG.Add(BannerList[i]);
                            else
                                engG.Add(BannerList[i]);
                        }
                        else
                        {
                            if(BannerList[i].Contains(langIdentifier))
                                lang.Add(BannerList[i]);
                            else
                                eng.Add(BannerList[i]);
                        }
                    }
                }

                try
                {
                if(langG.Count > 0) randImage = langG[new Random().Next(0, langG.Count)];
                else if(lang.Count > 0) randImage = lang[new Random().Next(0, lang.Count)];
                else if(engG.Count > 0) randImage = engG[new Random().Next(0, engG.Count)];
                else if(engG.Count > 0) randImage = eng[new Random().Next(0, eng.Count)];
                else return string.Empty;
                }

                //try
                //{
                //    randImage = BannerList[randomPick];
                //    if (preferGraphical && !randImage.Contains(graphicalBannerRecognizerSubstring))
                //    {
                //        // prefer graphical banners
                //        List<string> gBanners = new List<string>();
                //        foreach (string banner in BannerList)
                //            if (banner.Contains(graphicalBannerRecognizerSubstring))
                //                gBanners.Add(banner);
                //        if (gBanners.Count > 0)
                //            randImage = getRandomBanner(gBanners, false);
                //        // else no graphical banners avail. -> use text Banner we already picked
                //    }
                //}
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("Error getting random Image", MPTVSeriesLog.LogLevel.Normal);
                    return string.Empty;
                }
            }
            return Helper.PathCombine(Settings.GetPath(Settings.Path.banners), randImage);
        }

        public static List<DBValue> GetSingleField(string field, SQLCondition conds, DBTable obj)
        {

            string sql = "select " + field + " from " + obj.m_tableName + conds + conds.orderString + conds.limitString;
            List<DBValue> results = new List<DBValue>();
            try
            {
                foreach (SQLiteResultSet.Row result in DBTVSeries.Execute(sql).Rows)
                {
                    results.Add(result.fields[0]);
                }
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("GetSingleField SQL method generated an error: " + ex.Message);
            }
            return results;
        }

        static char[] splits = new char[] { '|', '\\', '/', ',' };

        /// <summary>
        /// For Genre etc. this method will split by each character in "splits"
        /// Should be used only for fields descriped in FieldsRequiringSplit
        /// </summary>
        /// <param name="fieldvalue"></param>
        /// <returns></returns>
        public static string[] splitField(string fieldvalue)
        {
            return fieldvalue.Split(splits, StringSplitOptions.RemoveEmptyEntries);
        }

    };

    public class SQLWhat
    {
        private String m_sWhat = String.Empty;
        private String m_sFrom = String.Empty;
        public SQLWhat()
        {

        }

        public SQLWhat(DBTable table)
        {
            Add(table);
        }

        public void Add(DBTable table)
        {
            AddWhat(table);
            if (m_sFrom.Length > 0)
                m_sFrom += ", ";
            m_sFrom += table.m_tableName;
        }

        public void AddWhat(DBTable table)
        {
            foreach (KeyValuePair<string, DBField> field in table.m_fields)
            {
                if (Helper.String.IsNullOrEmpty(m_sWhat))
                    m_sWhat += table.m_tableName + "." + field.Key;
                else
                    m_sWhat += ", " + table.m_tableName + "." + field.Key;
            }

        }

        public void Add(String sField)
        {
            if (m_sWhat.Length > 0)
                m_sWhat += ", ";
            m_sWhat += sField;
        }

        public static implicit operator String(SQLWhat what)
        {
            return what.m_sWhat + " from " + what.m_sFrom;
        }
    };

    public enum SQLConditionType
    {
        Equal,
        NotEqual,
        LessThan,
        LessEqualThan,
        GreaterThan,
        GreaterEqualThan,
        Like,
        In
    };

    public class SQLCondition
    {
        private String m_sConditions = String.Empty;
        private String m_sLimit = String.Empty;
        private String m_sOrderstring = String.Empty;
        bool _beginGroup = false;
        public void beginGroup()
        {
            _beginGroup = true;
        }

        public void endGroup()
        {
            m_sConditions += " ) ";
        }

        public bool limitIsSet = false;
        public bool customOrderStringIsSet = false;

        public bool nextIsOr = false;

        // I need this for subqueries
        /// <summary>
        /// Warning: do not set "where", also returns without "where"
        /// </summary>
        public string ConditionsSQLString
        {
            set
            {
                m_sConditions = value;
            }
            get
            {
                return m_sConditions;
            }
        }

        public enum orderType { Ascending, Descending };
        
        public SQLCondition()
        {
        }

        public SQLCondition(DBTable table, String sField, DBValue value, SQLConditionType type)
        {
            Add(table, sField, value, type);
        }

        public void AddSubQuery(string field, DBTable table, SQLCondition innerConditions, DBValue value, SQLConditionType type)
        {
            string sValue = value;
            if (type == SQLConditionType.Like)
              sValue = "'%" + ((String)value).Replace("'", "''") + "%'";
            else
               sValue = ((String)value).Replace("'", "''");

            AddCustom("( select " + field + " from " + table.m_tableName + innerConditions + innerConditions.orderString + innerConditions.limitString +  " ) ", sValue, type);
        }

        public void Add(DBTable table, String sField, DBValue value, SQLConditionType type)
        {
            if (table.m_fields.ContainsKey(sField))
            {
                String sValue = String.Empty;
                switch (table.m_fields[sField].Type)
                {
                    case DBField.cTypeInt:
                        sValue = value;
                        break;

                    case DBField.cTypeString:
                        if (type == SQLConditionType.Like)
                            sValue = "'%" + ((String)value).Replace("'", "''") + "%'";
                        else
                        sValue = "'" + ((String)value).Replace("'", "''") + "'";
                        break;
                }
                AddCustom(table.m_tableName + "." + sField, sValue, type);
            }
        }

        public void SetLimit(int limit)
        {
            m_sLimit = " limit " + limit.ToString();
            limitIsSet = true;
        }

        public string orderString
        {
            get { return m_sOrderstring; }
        }

        public string limitString
        {
            get { return m_sLimit; }
        }

        public void AddOrderItem(string qualifiedFieldname, orderType type)
        {
            if (m_sOrderstring.Length == 0)
                m_sOrderstring = " order by ";
            else m_sOrderstring += " , ";
            m_sOrderstring += qualifiedFieldname;
            m_sOrderstring += type == orderType.Ascending ? " asc " : " desc ";
            customOrderStringIsSet = true;
        }

        public void AddCustom(string what, string value, SQLConditionType type)
        {

                String sType = String.Empty;
                switch (type)
                {
                    case SQLConditionType.Equal:
                        sType = " = ";
                        break;

                    case SQLConditionType.NotEqual:
                        sType = " != ";
                        break;
                    case SQLConditionType.LessThan:
                        sType = " < ";
                        break;
                case SQLConditionType.LessEqualThan:
                    sType = " <= ";
                    break;
                    case SQLConditionType.GreaterThan:
                        sType = " > ";
                        break;
                case SQLConditionType.GreaterEqualThan:
                    sType = " >= ";
                    break;
                case SQLConditionType.Like:
                    sType = " like ";
                    break;
                case SQLConditionType.In:
                    sType = " in ";
                    break;
                }
            if(SQLConditionType.In == type) // reverse
                AddCustom(value + sType + what);
            else AddCustom(what + sType + value);
            }

        public void AddCustom(string SQLString)
        {
            if (m_sConditions.Length > 0 && SQLString.Length > 0)
            {
                if (nextIsOr)
                    m_sConditions += " or ";
                else
                    m_sConditions += " and ";
            }
            if (_beginGroup)
            {
                m_sConditions += " ( ";
                _beginGroup = false;
            }
            m_sConditions += SQLString;
        }

        public static implicit operator String(SQLCondition conditions)
        {
            return  conditions.m_sConditions.Length > 0 ? " where " + conditions.m_sConditions : conditions.m_sConditions;
        }

        public SQLCondition Copy()
        {
            SQLCondition copy = new SQLCondition();
            copy.customOrderStringIsSet = customOrderStringIsSet;
            copy.limitIsSet = limitIsSet;

            copy.m_sConditions = m_sConditions;
            copy.m_sLimit = m_sLimit;
            copy.m_sOrderstring = m_sOrderstring;
            return copy;
        }
    };

    // DB class - static, one instance only. 
    // holds the SQLLite object and the log object.
    public class DBTVSeries
    {
        #region private & init stuff
        private static SQLiteClient m_db = null;
        private static int m_nLogLevel = 0; // normal log = 0; debug log = 1;
        
        private static void InitDB()
        {
            String databaseFile = string.Empty;

            databaseFile = Settings.GetPath(Settings.Path.database);

            try
            {
                m_db = new SQLiteClient(databaseFile);

                m_db.Execute("PRAGMA cache_size=5000;\n");
                m_db.Execute("PRAGMA synchronous='OFF';\n");
                m_db.Execute("PRAGMA count_changes=1;\n");
                m_db.Execute("PRAGMA full_column_names=0;\n");
                m_db.Execute("PRAGMA short_column_names=0;\n");
                m_db.Execute("PRAGMA temp_store = MEMORY;\n");

                m_db.Execute("create index if not exists epComp1 ON local_episodes(CompositeID ASC)");
                m_db.Execute("create index if not exists epComp2 ON local_episodes(CompositeID2 ASC)");
                m_db.Execute("create index if not exists seriesIDLocal on local_series(ID ASC)");

                MPTVSeriesLog.Write("Successfully opened database '" + databaseFile + "'.");
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Failed to open database '" + databaseFile + "' (" + ex.Message + ").");
            }
        }
        #endregion
        public static void SetGlobalLogLevel(int nLevel)
        {
            m_nLogLevel = nLevel;
        }


        public static SQLiteResultSet Execute(String sCommand)
        {
            if (m_db == null)
            {
                InitDB();
            }
            SQLite.NET.SQLiteResultSet result;
            try
            {
                MPTVSeriesLog.Write("Executing SQL: ", sCommand, MPTVSeriesLog.LogLevel.Debug);
                result = m_db.Execute(sCommand);
                MPTVSeriesLog.Write("Success, returned Rows: ", result.Rows.Count, MPTVSeriesLog.LogLevel.Debug);
                return result;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Commit failed on this command: <" + sCommand + "> (" + ex.Message + ").");
                return new SQLiteResultSet();
            }
        }
    };
}
