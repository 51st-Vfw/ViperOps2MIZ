// ********************************************************************************************************************
//
// FileMiz.cs : DCS .miz file handling
//
// Copyright(C) 2025 ilominar/raven
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If not, see
// <https://www.gnu.org/licenses/>.
//
// ********************************************************************************************************************

#define xxxDEBUG_SAVE_LUA                                       // define to save mission lua file outside of .miz

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ViperOps2MIZ.Utility.LsonLib;

namespace ViperOps2MIZ.Utility.Files
{
    /// <summary>
    /// TODO: document
    /// </summary>
    public partial class FileMiz
    {
        /// <summary>
        /// TODO: document
        /// </summary>
        private class GroupInfo
        {
            public LsonDict Dict;
            public int Country;
            public int Group;
            public int GroupMax;

            public GroupInfo(LsonDict d, int ic = 0, int ig = 0, int igm = 0)
                => (Dict, Country, Group, GroupMax) = (d, ic, ig, igm);
        }

        private const string POINT_LUA = "point = {\n" +
                                         "  [\"alt\"] = 0,\n" +
                                         "  [\"action\"] = \"Turning Point\",\n" +
                                         "  [\"alt_type\"] = \"BARO\",\n" +
                                         "  [\"speed\"] = 138.88888888889,\n" +
                                         "  [\"task\"] = {\n" +
                                         "    [\"id\"] = \"ComboTask\",\n" +
                                         "    [\"params\"] = {\n" +
                                         "      [\"tasks\"] = { },\n" +
										 "	  },\n" +
										 "  },\n" +
                                         "  [\"name\"] = \"Untitled\",\n" +
                                         "  [\"type\"] = \"Turning Point\",\n" +
										 "  [\"ETA\"] = 0.0,\n" +
										 "  [\"ETA_locked\"] = false,\n" +
										 "  [\"y\"] = 0.0,\n" +
										 "  [\"x\"] = 0.0,\n" +
										 "  [\"speed_locked\"] = true,\n" +
										 "  [\"formation_template\"] = \"\",\n" +
										 "}\n";

        // ------------------------------------------------------------------------------------------------------------
        //
        // properties
        //
        // ------------------------------------------------------------------------------------------------------------

        public string PathIn { get; private set; }

        private Dictionary<string, LsonValue> Parsed { get; set; }

        private string Theater { get; set; }

        private bool IsUpdated { get; set; }

        private int GroupId { get; set; }

        private int UnitId { get; set; }

        // ------------------------------------------------------------------------------------------------------------
        //
        // construction
        //
        // ------------------------------------------------------------------------------------------------------------

        public FileMiz(string pathIn)
        {
            PathIn = pathIn;
            Parsed = [ ];
            Theater = "unknown";
            IsUpdated = false;
            GroupId = 5000;
            UnitId = 5000;

            string lua = FileManager.ReadFileFromZip(PathIn, "mission");
            Parsed = LsonVars.Parse(lua);

            Theater = Parsed["mission"].GetDict()["theatre"].GetString();
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // processing
        //
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// deserialize a lson string for the given element into an LsonDict. returns null on error.
        /// </summary>
        private static LsonDict Deserialize(string lson, string top) => LsonVars.Parse(lson)[top].GetDict();

        /// <summary>
        /// make a deep copy of a lson dictionary by serializing it and then deserializing it.
        /// </summary>
        private static LsonDict? DeepCopy(LsonDict? source)
        {
            if (source != null)
            {
                Dictionary<string, LsonValue> srcDict = new() { { "serialized", source } };
                return Deserialize(LsonVars.ToString(srcDict), "serialized");
            }
            return null;
        }

        /// <summary>
        /// returns the LsonDict at specified path referenced from root (nil => path is from root).
        /// </summary>
        private LsonDict? DictAtPath(string path, LsonDict? root = null)
        {
            List<string> elements = [.. path.Split('/') ];
            LsonDict? dict = root;
            if (string.IsNullOrEmpty(elements[0]))
            {
                dict = Parsed[elements[1]].GetDict();
                elements.RemoveAt(0);
                elements.RemoveAt(0);
            }
            foreach (string element in elements)
                if (int.TryParse(element, out int index))
                    dict = dict?[index].GetDict();
                else
                    dict = dict?[element].GetDict();

            return dict;
        }

        /// <summary>
        /// TODO: document
        /// </summary>
        private Dictionary<int, LsonDict> DictArrayAtPath(string path, LsonDict? root = null)
        {
            Dictionary<int, LsonDict> list = [ ];
            LsonDict? dict = DictAtPath(path, root);
            if (dict != null)
                foreach (KeyValuePair<LsonValue, LsonValue> kvp in dict)
                    list[kvp.Key.GetIntLenient()] = kvp.Value.GetDict();
            return list;
        }


        /// <summary>
        /// TODO: document
        /// </summary>
        private Dictionary<string, GroupInfo> BuildGroupMap(string coalition, string type)
        {
            Dictionary<string, GroupInfo> map = [ ];

            // /mission/coalition/<coa>/country/<i>/vehicle/group/<j>/name

            // want max(j) for (<coa>, <i>)

            Dictionary<int, LsonDict> countries = DictArrayAtPath($"/mission/coalition/{coalition}/country");
            foreach (KeyValuePair<int, LsonDict> kvpCountry in countries)
            {
                Dictionary<int, LsonDict> groups = DictArrayAtPath($"{type}/group", kvpCountry.Value);
                int max = 0;
                foreach (int index in groups.Keys)
                    max = Math.Max(max, index);
                foreach (KeyValuePair<int, LsonDict> kvpGroup in groups)
                {
                    string name = kvpGroup.Value["name"].GetString();
                    map[name] = new GroupInfo(kvpGroup.Value, kvpCountry.Key, kvpGroup.Key, max);
                }
            }

            return map;
        }

        /// <summary>
        /// update the bullseye coordiante at mission/coalition/blue/bullseye to match the kml bullseye.
        /// </summary>
        private void UpdateBullseye(CoordKml bullsCoord)
        {
            CoordXZ xz = CoordInterpolator.Instance.LLtoXZ(Theater, bullsCoord.Lat, bullsCoord.Lon);
            //
            // NOTE: coordinate mapping here xz.X --> bullseye.x, xz.Z --> bullseye.y
            //
            LsonDict? bullseye = DictAtPath("/mission/coalition/blue/bullseye");
            if (bullseye != null )
            {
                bullseye["x"] = xz.X;
                bullseye["y"] = xz.Z;
            }
        }

        /// <summary>
        /// TODO: document
        /// </summary>
        private void AddRedVehicleGroup(GroupInfo info, CoordKml coord, int numRedGroupAdded)
        {
            CoordXZ xz = CoordInterpolator.Instance.LLtoXZ(Theater, coord.Lat, coord.Lon);

            LsonDict? groupList = DictAtPath($"/mission/coalition/red/country/{info.Country}/vehicle/group");
            if (groupList != null)
            {
                LsonDict? newGroup = DeepCopy(groupList[info.Group].GetDict());
                if (newGroup != null)
                {
                    double x = newGroup["x"].GetDouble();
                    double y = newGroup["y"].GetDouble();

                    groupList.Add(info.GroupMax + numRedGroupAdded, newGroup);

                    // update top-level group properties.
                    //
                    // name           => set name to "<group_name>#V2M.<n>" to make unique group name
                    // lateActivation => force "false"
                    // groupId        => update with unique group id
                    // x, y           => update to kml-specified location
                    //
                    newGroup["name"] = $"{newGroup["name"].GetString()}#V2M.{numRedGroupAdded}";
                    newGroup["lateActivation"] = false;
                    newGroup["groupId"] = GroupId++;
                    newGroup["x"] = xz.X;
                    newGroup["y"] = xz.Z;

                    // update "route" property by removing "spans" and all but the first "points".
                    //
                    // [spans]               => all spans deleted
                    // [points][1] x, y, alt => update to kml-specified location
                    //
                    newGroup["route"].GetDict().Remove("spans");
                    newGroup["route"].Add("spans", new LsonDict());

                    LsonDict? point1 = newGroup["route"].GetDict()["points"].GetDict()[1].GetDict();
                    newGroup["route"].GetDict().Remove("points");
                    newGroup["route"].Add("points", new LsonDict());
                    if (point1 != null)
                    {
                        point1["alt"] = coord.Alt;
                        point1["x"] = xz.X;
                        point1["y"] = xz.Z;
                        newGroup["route"].GetDict()["points"].GetDict().Add(1, point1);
                    }

                    // update all units in group
                    //
                    // name   => set name to "<unit_name>#V2M.<n>" to make unique unit name
                    // unitId => update with unique unit id
                    // x, y   => update to kml-specified location, maintaining relative position within group
                    //
                    foreach (KeyValuePair<LsonValue, LsonValue> kvp in newGroup["units"].GetDict())
                    {
                        LsonDict newUnit = kvp.Value.GetDict();
                        newUnit["name"] = $"V2M {newUnit["name"].GetString()}#{numRedGroupAdded}";
                        newUnit["x"] = xz.X + (newUnit["x"].GetDouble() - x);
                        newUnit["y"] = xz.Z + (newUnit["y"].GetDouble() - y);
                        newUnit["unitId"] = UnitId++;
                    }
                }
            }
        }

        /// <summary>
        /// TODO: document
        /// </summary>
        private void AddBlueJetSteerpoints(GroupInfo info, List<CoordKml> stpts)
        {
            LsonDict points = info.Dict["route"].GetDict()["points"].GetDict();
            int iStpt = 2;
            foreach (CoordKml coord in stpts)
            {
                CoordXZ xz = CoordInterpolator.Instance.LLtoXZ(Theater, coord.Lat, coord.Lon);
                
                LsonDict point = Deserialize(POINT_LUA, "point");
                if (string.IsNullOrEmpty(coord.Name))
                    point.Remove("name");
                else
                    point["name"] = coord.Name;
                point["alt"] = coord.Alt;
                point["x"] = xz.X;
                point["y"] = xz.Z;
                if (points.ContainsKey(iStpt))
                    points[iStpt++] = point;
                else
                    points.Add(iStpt++, point);
            }
        }

        /// <summary>
        /// update the internal representation of the source .miz file to incorporate the changes from the
        /// kml file.
        /// </summary>
        public void BuildMissionWithKml(FileKml kml)
        {
            if (IsUpdated)
                return;

            CoordKml? bulls = kml.Bullseye;
            if (bulls != null)
                UpdateBullseye(bulls);

            // TODO: anything to do for enemy airbases?

            Dictionary<string, GroupInfo> groupsRed = BuildGroupMap("red", "vehicle");
            int numRedGroupAdded = 1;
            foreach (CoordKml ewr in kml.EWRs)
                if (!string.IsNullOrEmpty(ewr.Name) && groupsRed.ContainsKey(ewr.Name))
                    AddRedVehicleGroup(groupsRed[ewr.Name], ewr, numRedGroupAdded++);
            foreach (CoordKml sam in kml.SAMs)
                if (!string.IsNullOrEmpty(sam.Name) && groupsRed.ContainsKey(sam.Name))
                    AddRedVehicleGroup(groupsRed[sam.Name], sam, numRedGroupAdded++);

            Dictionary<string, GroupInfo> groupsBlue = BuildGroupMap("blue", "plane");
            foreach (KeyValuePair<string, List<CoordKml>> kvp in kml.Jets)
                if (groupsBlue.ContainsKey(kvp.Key))
                    AddBlueJetSteerpoints(groupsBlue[kvp.Key], kvp.Value);

            IsUpdated = true;
        }

        /// <summary>
        /// save the updated mission to disk. the new .miz is first copied from the original input .miz that
        /// was processed, then the "mission" file within the .miz is replaced with the edited version that
        /// BuildMissionWithKml generates.
        /// </summary>
        public void SaveMission(string pathOut)
        {
            FileManager.CopyFile(PathIn, pathOut);
            string data = LsonVars.ToString(Parsed) ?? throw new Exception("Unable to rebuild mission data.");
            FileManager.WriteDataToZip(pathOut, "mission", data.Replace("\r\n", "\n"));

#if DEBUG_SAVE_LUA
            File.WriteAllText("c:\\Users\\twillis\\Desktop\\viperops_lson_debug.lua", data);
#endif
        }
    }
}