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
            public string Type;
            public int Country;
            public int Group;

            public GroupInfo(LsonDict d, string t, int ic = 0, int ig = 0)
                => (Dict, Type, Country, Group) = (d, t, ic, ig);
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
            Parsed = [];
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
            List<string> elements = [.. path.Split('/')];
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
            Dictionary<int, LsonDict> list = [];
            LsonDict? dict = DictAtPath(path, root);
            if (dict != null)
                foreach (KeyValuePair<LsonValue, LsonValue> kvp in dict)
                    list[kvp.Key.GetIntLenient()] = kvp.Value.GetDict();
            return list;
        }

        /// <summary>
        /// catalogs all groups from /mission/coalition/{coalition}/country/*/{type}/group in the parsed lson
        /// returning a dictionay that maps the group's name onto a GroupInfo for the group.
        /// </summary>
        private Dictionary<string, GroupInfo> BuildGroupMap(string coalition, List<string> types)
        {
            Dictionary<string, GroupInfo> map = [];
            foreach (string type in types) {
                Dictionary<int, LsonDict> countries = DictArrayAtPath($"/mission/coalition/{coalition}/country");
                foreach (KeyValuePair<int, LsonDict> kvpCountry in countries)
                {
                    Dictionary<int, LsonDict> groups = DictArrayAtPath($"{type}/group", kvpCountry.Value);
                    foreach (KeyValuePair<int, LsonDict> kvpGroup in groups)
                    {
                        string name = kvpGroup.Value["name"].GetString();
                        map[name] = new GroupInfo(kvpGroup.Value, type, kvpCountry.Key, kvpGroup.Key);
                    }
                }
            }
            return map;
        }

        /// <summary>
        /// update the bullseye coordiante at mission/coalition/blue/bullseye to match the kml bullseye.
        /// </summary>
        private void UpdateBullseye(CoordKml bullsCoord)
        {
            LsonDict bullseye = DictAtPath("/mission/coalition/blue/bullseye")
                                ?? throw new Exception("Unable to find bullseye in mission.");
            CoordXZ xz = CoordInterpolator.Instance.LLtoXZ(Theater, bullsCoord.Lat, bullsCoord.Lon)
                         ?? throw new Exception($"Mission theater {Theater} is not supported.");
            //
            // NOTE: coordinate mapping here xz.X --> bullseye.x, xz.Z --> bullseye.y
            //
            bullseye["x"] = xz.X;
            bullseye["y"] = xz.Z;
        }

        /// <summary>
        /// update top-level name, lateActivation, groupId, x, and y properties of a group.
        /// </summary>
        private static void UpdateGroupTopLevel(LsonDict group, CoordXZ xzNew, int groupId)
        {
            group["name"] = $"{group["name"].GetString()}#V2M.{groupId}";
            group["groupId"] = groupId;
            group["x"] = xzNew.X;
            group["y"] = xzNew.Z;
        }

        /// <summary>
        /// update route/points of a group. we remove all points except points[1] and update the alt, x, and y
        /// properties of points[1].
        /// </summary>
        private static void UpdateGroupRoutePoints(LsonDict group, CoordXZ xzNew, double alt)
        {
            LsonDict point1 = group["route"].GetDict()["points"].GetDict()[1].GetDict()
                              ?? throw new Exception($"Unable to find points[1] in group.");

            point1["alt"] = alt;
            point1["x"] = xzNew.X;
            point1["y"] = xzNew.Z;

            group["route"].GetDict().Remove("points");
            group["route"].Add("points", new LsonDict());
            group["route"].GetDict()["points"].GetDict().Add(1, point1);
        }

        /// <summary>
        /// update units of a group. we update the name, x, y, and unitId properties of each unit.
        /// </summary>
        private void UpdateGroupUnits(LsonDict group, CoordXZ xzNew, CoordXZ xzTmplt, int groupId)
        {
            foreach (KeyValuePair<LsonValue, LsonValue> kvp in group["units"].GetDict())
            {
                LsonDict unit = kvp.Value.GetDict();
                unit["name"] = $"V2M {unit["name"].GetString()}#V2M.{groupId}";
                unit["x"] = xzNew.X + (unit["x"].GetDouble() - xzTmplt.X);
                unit["y"] = xzNew.Z + (unit["y"].GetDouble() - xzTmplt.Z);
                unit["unitId"] = UnitId++;
            }
        }

        /// <summary>
        /// TODO: document
        /// </summary>
        private CoordXZ AddRedGroup(GroupInfo info, CoordKml llaNew, CoordXZ xzBaseTmplt)
        {
            LsonDict groupList = DictAtPath($"/mission/coalition/red/country/{info.Country}/{info.Type}/group")
                                 ?? throw new Exception($"Unable to find group for {info.Type} in country {info.Country}");
            LsonDict newGroup = DeepCopy(groupList[info.Group].GetDict())
                                ?? throw new Exception($"Unable to copy template group");

            CoordXZ xzTmplt = new()
            {
                X = newGroup["x"].GetDouble(),
                Z = newGroup["y"].GetDouble()
            };
            CoordXZ xzNew = CoordInterpolator.Instance.LLtoXZ(Theater, llaNew.Lat, llaNew.Lon)
                            ?? throw new Exception($"Mission theater {Theater} is not supported.");
            if ((xzBaseTmplt.X != 0.0) || (xzBaseTmplt.Z != 0.0))
            {
                xzNew.X += (xzTmplt.X - xzBaseTmplt.X);
                xzNew.Z += (xzTmplt.Z - xzBaseTmplt.Z);
            }
            int index = 1;
            foreach (LsonValue key in groupList.Keys)
                index = Math.Max(index, key.GetInt() + 1);
            groupList.Add(index, newGroup);

            UpdateGroupTopLevel(newGroup, xzNew, GroupId);
            UpdateGroupUnits(newGroup, xzNew, xzTmplt, GroupId);
            UpdateGroupRoutePoints(newGroup, xzNew, llaNew.Alt);

            if (string.Equals(info.Type, "vehicle"))
            {
                newGroup["lateActivation"] = false;

                newGroup["route"].GetDict().Remove("spans");
                newGroup["route"].Add("spans", new LsonDict());
            }

            GroupId++;

            return xzTmplt;
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
                CoordXZ xz = CoordInterpolator.Instance.LLtoXZ(Theater, coord.Lat, coord.Lon)
                             ?? throw new Exception($"Mission theater {Theater} is not supported.");

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

            Dictionary<string, GroupInfo> groupsRed = BuildGroupMap("red", [ "vehicle", "static" ]);
            List<CoordKml> coords = kml.EWRs;
            coords.AddRange(kml.SAMs);
            coords.AddRange(kml.Markers);
            foreach (CoordKml coord in coords)
                if (!string.IsNullOrEmpty(coord.Name) && groupsRed.TryGetValue(coord.Name, out GroupInfo? info))
                {
                    CoordXZ xzTmplt = AddRedGroup(info, coord, new CoordXZ());
                    foreach (string key in groupsRed.Keys)
                        if (!string.Equals(key, coord.Name) &&
                             string.Equals(groupsRed[key].Type, "static") &&
                            key.StartsWith($"{coord.Name} SG-"))
                        {
                            // coord = offset between template coordinates of unit and static
                            // TODO: need to adjust based on offset from template center of related group
                            AddRedGroup(groupsRed[key], coord, xzTmplt);
                        }
                }

            Dictionary<string, GroupInfo> groupsBlue = BuildGroupMap("blue", [ "plane" ]);
            foreach (KeyValuePair<string, List<CoordKml>> kvp in kml.Jets)
                if (groupsBlue.TryGetValue(kvp.Key, out GroupInfo? info))
                    AddBlueJetSteerpoints(info, kvp.Value);

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