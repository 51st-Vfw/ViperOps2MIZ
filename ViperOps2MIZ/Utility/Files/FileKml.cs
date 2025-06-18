// ********************************************************************************************************************
//
// FileKml.cs : ViperOps .kml file handling
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Xml;

namespace ViperOps2MIZ.Utility.Files
{
    /// <summary>
    /// coordinate in kml consisting of name and lat/lon/alt.
    /// </summary>
    public sealed class CoordKml
    {
        public string? Name { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Alt { get; set; }

        public CoordKml(string name, string coordTuple)
        {
            Name = name;
            // TODO: error handling here? what if kvp.Value is borked?
            List<string> fields = [.. coordTuple.Split(",") ];
            Lon = double.Parse(fields[0]);
            Lat = double.Parse(fields[1]);
            Alt = double.Parse(fields[2]);
        }
    }

    /// <summary>
    /// TODO - document
    ///
    /// viper ops .kml files encodes each route, sam, ewr, or enemy airfield in a "/Document/Folder" node
    /// with a coordinate tuple in a "coordinates" nodes in the format "<longitude>,<latitude>,<altitude>".
    /// 
    /// for routes, "/Document/Folder/name" defines the name of the route. the folder may contain one or
    /// more "/Document/Folder/Placemark" nodes that define the route and any custom labels added to points
    /// along the route.
    /// 
    ///   - "/Document/Folder/Placemark/LineString/coordinates" defines all of the coordinates in the route
    ///     as a space-separated list of coordinate tuples.
    ///   - "/Document/Folder/Placemark/Point/name" defines the custom name of a point along the route
    ///     with "/Document/Folder/Placemark/Point/coordinates" providing a coordinate tuple for the name.
    /// 
    /// for non-routes, "/Document/Folder/name" defines the type of object the folder contains (either "SAM",
    /// "EW Radar", "Enemy Airfield", or "Bullseye". each of these folders has one or more
    /// "/Document/Folder/Placemark" nodes that define the location(s) of objects.
    /// 
    ///   - "/Document/Folder/Placemark/Point/name" defines the custom name of the object with
    ///     "/Document/Folder/Placemark/Point/coordinates" providing a coordinate tuple for the object.
    ///  
    /// for bullseye, "/Document/Placemark" defines the location.
    /// 
    ///   - "/Document/Folder/Placemark/Point/name" is "Bullseye" for the bullseye
    ///     "/Document/Folder/Placemark/Point/coordinates" providing a coordinate tuple for the bullseye.
    ///
    /// for unassigned points, "/Document/Placemark" defines the location.
    /// 
    ///   - "/Document/Folder/Placemark/Point/name" is the name of the point.
    ///     "/Document/Folder/Placemark/Point/coordinates" providing a coordinate tuple for the point.
    ///
    /// all objects except "Bullseye" can have more than one placemark.
    /// </summary>
    public partial class FileKml
    {
        // ------------------------------------------------------------------------------------------------------------
        //
        // properties
        //
        // ------------------------------------------------------------------------------------------------------------

        public string Path { get; private set; }

        public CoordKml? Bullseye { get; private set; }

        public List<CoordKml> Markers { get; private set; }

        public List<CoordKml> EWRs { get; private set; }

        public List<CoordKml> SAMs { get; private set; }

        public List<CoordKml> EnemyAFBs { get; private set; }

        public Dictionary<string, List<CoordKml>> Jets { get; private set; }

        // ------------------------------------------------------------------------------------------------------------
        //
        // construction
        //
        // ------------------------------------------------------------------------------------------------------------

        public FileKml(string path)
        {
            Path = path;

            Bullseye = null;
            Markers = [ ];
            EWRs = [ ];
            SAMs = [ ];
            EnemyAFBs = [ ];
            Jets = [ ];

            XmlDocument doc = new();
            doc.Load(Path);
            if (doc.DocumentElement != null)
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    if (string.Equals(node.Name, "Document"))
                        IngestDocument(node.ChildNodes);
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // data accessors
        //
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// converts a list of { name, kml coordinate tuple } key/value pairs into a list of CoordKml instaces with
        /// Name matching the key and Alt/Lat/Lon extracted from the coordinate tuple.
        /// </summary>
        private static List<CoordKml> DataToCoordList(List<KeyValuePair<string, string>> input)
        {
            List<CoordKml> list = [ ];
            foreach (KeyValuePair<string, string> kvp in input)
                list.Add(new CoordKml(kvp.Key, kvp.Value));
            return list;
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // ingest .kml files
        //
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// returns inner text of the single node at the xml path from a parent node, "" if no such node exists.
        /// </summary>
        private static string GetInnerTextAtPath(XmlNode parent, string path)
        {
            XmlNode? child = parent.SelectSingleNode(path);
            return child != null ? child.InnerText : "";
        }

        /// <summary>
        /// returns inner text of the child "name" node from a parent node, "" if no such node exists.
        /// </summary>
        private static string GetChildNameNodeText(XmlNode parent)
            => GetInnerTextAtPath(parent, "*[local-name()='name']");

        /// <summary>
        /// TODO - document
        /// </summary>
        private static List<KeyValuePair<string, string>> ExtractChildrenFromFolder(XmlNode folder, string path)
        {
            List<KeyValuePair<string, string>> list = [];
            XmlNodeList? pmarks = folder.SelectNodes("*[local-name()='Placemark']");
            if (pmarks != null)
                foreach (XmlNode pmark in pmarks)
                {
                    string name = GetChildNameNodeText(pmark);
                    string coord = GetInnerTextAtPath(pmark, path);
                    if (!string.IsNullOrEmpty(coord))
                        list.Add(new KeyValuePair<string, string>(name, coord.Trim()));
                }

            return list;
        }

        /// <summary>
        /// TODO - document
        /// </summary>
        private static List<KeyValuePair<string, string>> ExtractPointsFromPlacemark(XmlNode pmark)
        {
            List<KeyValuePair<string, string>> list = [];
            XmlNodeList? xnlPoints = pmark.SelectNodes("*[local-name()='Point']");
            if (xnlPoints != null)
                foreach (XmlNode node in xnlPoints)
                {
                    string name = GetChildNameNodeText(node);
                    string coord = GetInnerTextAtPath(node, "*[local-name()='coordinates']");
                    if (!string.IsNullOrEmpty(coord))
                        list.Add(new KeyValuePair<string, string>(name, coord.Trim()));
                }
            return list;
        }

        /// <summary>
        /// TODO - document
        /// </summary>
        private static List<KeyValuePair<string, string>> ExtractPointsFromFolder(XmlNode folder)
            => ExtractChildrenFromFolder(folder, "*[local-name()='Point']/*[local-name()='coordinates']");

        /// <summary>
        /// TODO - document
        /// </summary>
        private static List<KeyValuePair<string, string>> ExtractLinesFromFolder(XmlNode folder)
            => ExtractChildrenFromFolder(folder, "*[local-name()='LineString']/*[local-name()='coordinates']");

        /// <summary>
        /// TODO - document
        /// 
        /// NOTE: call only once
        /// </summary>
        private void IngestDocument(XmlNodeList children)
        {
            foreach (XmlNode node in children)
            {
                string nodeNameChild = GetChildNameNodeText(node);
                if (string.Equals(node.Name, "Placemark") && string.Equals(nodeNameChild, "Bullseye"))
                {
                    List<KeyValuePair<string, string>> points = ExtractPointsFromPlacemark(node);
                    if (points.Count != 1)
                        throw new Exception("Found incorrectly formatted bullseye element?");
                    //
                    // NOTE: bullseye returns with a name of "", we'll change it to something useful here.
                    //
                    Bullseye = new CoordKml("BULLS", points[0].Value);
                }
                else if (string.Equals(node.Name, "Placemark"))
                {
                    List<KeyValuePair<string, string>> points = ExtractPointsFromPlacemark(node);
                    if (points.Count != 1)
                        throw new Exception("Found incorrectly formatted unassigned marker element?");
                    Markers.Add(new CoordKml(nodeNameChild, points[0].Value));
                }
                else if (string.Equals(node.Name, "Folder") && string.Equals(nodeNameChild, "SAM"))
                {
                    SAMs.AddRange(DataToCoordList(ExtractPointsFromFolder(node)));
                }
                else if (string.Equals(node.Name, "Folder") && string.Equals(nodeNameChild, "EW Radar"))
                {
                    EWRs.AddRange(DataToCoordList(ExtractPointsFromFolder(node)));
                }
                else if (string.Equals(node.Name, "Folder") && string.Equals(nodeNameChild, "Enemy Airfield"))
                {
                    EnemyAFBs.AddRange(DataToCoordList(ExtractPointsFromFolder(node)));
                }
                else if (string.Equals(node.Name, "Folder"))
                {
                    List<KeyValuePair<string, string>> labels = ExtractPointsFromFolder(node);
                    List<KeyValuePair<string, string>> route = ExtractLinesFromFolder(node);
                    if (route.Count != 1)
                        throw new Exception("Found incorrectly formatted route element?");
                    else if (Jets.ContainsKey(route[0].Key))
                        throw new Exception($"Route for {route[0].Key} is defined twice?");

                    Dictionary<string, string> labelMap = [];
                    foreach (KeyValuePair<string, string> kvp in labels)
                        labelMap[kvp.Value] = kvp.Key;

                    List<CoordKml> stpts = [ ];
                    List<string> routePoints = [.. route[0].Value.Split(" ") ];
                    int nStpt = 1;
                    foreach (string point in routePoints)
                    {
                        string stptName = (labelMap.ContainsKey(point)) ? labelMap[point] : $"SP{nStpt}";
                        stpts.Add(new CoordKml(stptName, point));
                        nStpt++;
                    }
                    Jets[route[0].Key] = stpts;
                }
            }
        }
    }
}
