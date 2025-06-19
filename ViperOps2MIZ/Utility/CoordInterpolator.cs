// ********************************************************************************************************************
//
// CoordInterpolator.cs -- dcs coordinate interpolator
//
// Copyright(C) 2020-2022 project tauntaun contributors
// Copyright(C) 2023-2025 ilominar/raven
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

namespace ViperOps2MIZ.Utility
{
    /// <summary>
    /// dcs planar 2d surface coordinate (does not contain altitude/elevation information).
    /// </summary>
    public class CoordXZ
    {
        public double X;            // northing
        public double Z;            // easting
    }

    /// <summary>
    /// latitude/longitude surface coordinate (does not contain altitude/elevation information). coordiantes
    /// are in decimal degrees or radians depending on api.
    /// </summary>
    public class CoordLL
    {
        public double Lat;          // northing
        public double Lon;          // easting
    }

    /// <summary>
    /// singleton coordinate interpolator to move between x/z and lat/lon coordinates in dcs.
    /// 
    /// many thanks to raus of gv5js datacard fame who pointed me at project tauntaun for the conversion code
    /// and setup (and thanks to project tauntaun for the original implementation). key pieces of this
    /// algorithm are described in the following code on github:
    /// 
    /// https://github.com/UOAF/project-tauntaun/tree/master/tools/coords
    /// https://github.com/UOAF/project-tauntaun/blob/master/tauntaun_live_editor/coord.py
    /// 
    /// code here is largely the same, ported to C#. basically, the code uses pre-computed offsets to allow
    /// mapping between the x/z and lat/lon coordiante systems. precomputed offsets are fit to dcs off-line
    /// to be included here as constants.
    /// </summary>
    public sealed class CoordInterpolator
    {
        private static readonly Lazy<CoordInterpolator> lazy = new(() => new CoordInterpolator());
        public static CoordInterpolator Instance { get => lazy.Value; }

        /// <summary>
        /// theater information including computed offsets to transform between X/Z and Lat/Lon coordinates. 
        /// </summary>
        private class TheaterInfo
        {
            public double Dx;           // northing delta for dcs x to utm northing
            public double Dz;           // easting delta for dcs z to utm easting
            public int Zone;
            public bool IsSouthHemi;

            public TheaterInfo(double dx, double dz, int zone, bool isSouthHemi)
                => (Dx, Dz, Zone, IsSouthHemi) = (dx, dz, zone, isSouthHemi);
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // properties
        //
        // ------------------------------------------------------------------------------------------------------------

        double UTMScaleFactor { get; set; }

        double SMa { get; set; }
        
        double SMb { get; set; }

        // NOTE: sm values changed to match DCS
        // NOTE: original sm values: 6378245, 6356863
        const double SM_A_DEFAULT = 6375585.50700497;
        const double SM_B_DEFAULT = 6354209.24672509;

        // internal theater inforamtion dictionary mapping a DCS theater (from the theater file embedded in the .miz)
        // onto a corresponding TheaterInfo instance. x/z values in the terrain info are pre-computed based on grid
        // inforamtion generated from dcs.
        //
        private readonly Dictionary<string, TheaterInfo> _theaterInfo = new()
        {
            ["Afghanistan"] = new TheaterInfo(3759656.73273285, 300149.98159237, 41, false),
            ["Caucasus"] = new TheaterInfo(4998114.6775109, 99517.01067793, 36, false),
            ["Falklands"] = new TheaterInfo(4184583.3670, -147639.8755, 21, true),
            ["Germany"] = new TheaterInfo(6061632.75443205, -35427.57613432, 34, false),
            ["Iraq"] = new TheaterInfo(3680056.7536012, -72289.97515422, 38, false),
            ["Kola"] = new TheaterInfo(7543624.54780873, 62701.93823161, 34, false),
            ["MarianaIslands"] = new TheaterInfo(1491839.88704271, -238417.99059562, 55, false),
            ["Nevada"] = new TheaterInfo(4410027.78012357, 193996.80821451, 11, false),
            ["PersianGulf"] = new TheaterInfo(2894932.78443276, -75755.99875273, 40, false),
            ["SinaiMap"] = new TheaterInfo(3325312.76592359, -169221.99957107, 36, false),
            ["Syria"] = new TheaterInfo(3879865.72585971, -282800.99275397, 37, false),
        };

        // ------------------------------------------------------------------------------------------------------------
        //
        // construction
        //
        // ------------------------------------------------------------------------------------------------------------

        public CoordInterpolator(double sma = SM_A_DEFAULT, double smb = SM_B_DEFAULT)
            => (UTMScaleFactor, SMa, SMb) = (1.0, sma, smb);

        // ------------------------------------------------------------------------------------------------------------
        //
        // methods
        //
        // ------------------------------------------------------------------------------------------------------------

        // convert lat/lon coordinates (in decimal degrees) in a particular theater to x/z planar dcs map coordinates.
        // returns null if the theater is unknown.
        //
        public CoordXZ? LLtoXZ(string theater, double lat, double lon)
        {
            if (_theaterInfo.TryGetValue(theater, out TheaterInfo? info))
            {
                CoordXZ xz = LatLonToUTMXY(lat * (Math.PI / 180.0), lon * (Math.PI / 180.0), info.Zone);
                //
                // NOTE: mind the coordinate flip here: x/y in UTM are z/x in DCS
                //
                CoordXZ xzDCS = new() {
                    X = xz.Z - info.Dx,
                    Z = xz.X - info.Dz
                };
                return xzDCS;
#if NOPE
                def lat_lon_to_xz(self, c, lat, lon):
                utmxy, zone = self.LatLonToUTMXY(math.radians(lat), math.radians(lon), c['zone'])

            return utmxy[1] - c['x'], utmxy[0] - c['z']

                c = _terrain_data[terrain]
                return _instance.lat_lon_to_xz(c, lat, lon)
#endif
            }
            return null;
        }

        // convert x/z planar dcs map coordinates in a particular theater to lat/lon coordinates (in decimal
        // degrees). returns null if the theater is unknown.
        //
        public CoordLL? XZtoLL(string theater, double x, double z)
        {
            if (_theaterInfo.TryGetValue(theater, out TheaterInfo? info))
            {
                //
                // NOTE: mind the coordinate flip here: x/y in UTM are z/x in DCS
                //
                CoordLL ll = UTMXYToLatLon(z + info.Dz, x + info.Dx, info.Zone, info.IsSouthHemi);
                ll.Lat *= 180.0 / Math.PI;
                ll.Lon *= 180.0 / Math.PI;
                return ll;
            }
            return null;
        }

        // Determines the central meridian for the given UTM zone.
        //
        // Inputs:
        //   zone - An integer value designating the UTM zone, range[1, 60].
        //
        // Returns:
        //   The central meridian for the given UTM zone, in radians, or zero if the UTM zone parameter is outside
        //   the range[1, 60].
        //
        //   Range of the central meridian is the radian equivalent of [-177, +177].
        //
        private static double UTMCentralMeridian(int zone)
        {
            return (-183.0 + zone * 6.0) * (Math.PI / 180.0);
        }

        // Computes the ellipsoidal distance from the equator to a point at a given latitude.
        //
        // Reference: Hoffmann-Wellenhof, B., Lichtenegger, H., and Collins, J.,
        // GPS: Theory and Practice, 3rd ed. New York: Springer-Verlag Wien, 1994.
        //
        // Inputs:
        //   phi - Latitude of the point, in radians.
        //
        // Globals:
        //   sm_a - Ellipsoid model major axis.
        //   sm_b - Ellipsoid model minor axis.
        //
        // Returns:
        //   The ellipsoidal distance of the point from the equator, in meters.
        //
        private double ArcLengthOfMeridian(double phi)
        {
            // Precalculate n
            double n = (SMa - SMb) / (SMa + SMb);

            // Precalculate alpha
            double alpha = (SMa + SMb) / 2 * (1.0 + Math.Pow(n, 2.0) / 4.0 + Math.Pow(n, 4.0) / 64.0);

            // Precalculate beta
            double beta = -3.0 * n / 2.0 + 9.0 * (Math.Pow(n, 3.0) / 16.0) + -3.0 * (Math.Pow(n, 5.0) / 32.0);

            // Precalculate gamma
            double gamma = 15.0 * Math.Pow(n, 2.0) / 16.0 + -15.0 * Math.Pow(n, 4.0) / 32.0;

            // Precalculate delta
            double delta = -35.0 * Math.Pow(n, 3.0) / 48.0 + 105.0 * Math.Pow(n, 5.0) / 256.0;

            // Precalculate epsilon
            double epsilon = 315.0 * (Math.Pow(n, 4.0) / 512.0);

            // Now calculate the sum of the series and return
            return alpha * (phi + beta * Math.Sin(2.0 * phi)
                                + gamma * Math.Sin(4.0 * phi)
                                + delta * Math.Sin(6.0 * phi)
                                + epsilon * Math.Sin(8.0 * phi));
        }

        // Computes the footpoint latitude for use in converting transverse Mercator coordinates to ellipsoidal
        // coordinates.
        //
        // Reference: Hoffmann-Wellenhof, B., Lichtenegger, H., and Collins, J.,
        // GPS: Theory and Practice, 3rd ed. New York: Springer-Verlag Wien, 1994.
        //
        // Inputs:
        //   y - The UTM northing coordinate, in meters.
        //
        // Returns:
        //   The footpoint latitude, in radians.
        //
        private double FootpointLatitude(double y)
        {
            // Precalculate n (Eq. 10.18)
            double n = (SMa - SMb) / (SMa + SMb);

            // Precalculate alpha_ (Eq. 10.22, Same as alpha in Eq. 10.17)
            double alpha_ = (SMa + SMb) / 2.0 * (1 + Math.Pow(n, 2.0) / 4.0 + Math.Pow(n, 4.0) / 64.0);

            // Precalculate y_ (Eq. 10.23)
            double y_ = y / alpha_;

            // Precalculate beta_ (Eq. 10.22)
            double beta_ = 3.0 * n / 2.0 + -27.0 * (Math.Pow(n, 3.0) / 32.0) + 269.0 * (Math.Pow(n, 5.0) / 512.0);

            // Precalculate gamma_ (Eq. 10.22)
            double gamma_ = 21.0 * Math.Pow(n, 2.0) / 16.0 + -55.0 * Math.Pow(n, 4.0) / 32.0;

            // Precalculate delta_ (Eq. 10.22)
            double delta_ = 151.0 * Math.Pow(n, 3.0) / 96.0 + -417.0 * Math.Pow(n, 5.0) / 128.0;

            // Precalculate epsilon_ (Eq. 10.22)
            double epsilon_ = 1097.0 * Math.Pow(n, 4.0) / 512.0;

            // Now calculate the sum of the series (Eq. 10.21)
            return y_ + beta_ * Math.Sin(2.0 * y_)
                      + gamma_ * Math.Sin(4.0 * y_)
                      + delta_ * Math.Sin(6.0 * y_)
                      + epsilon_ * Math.Sin(8.0 * y_);
        }

        // Converts a latitude/longitude pair to x and y coordinates in the Transverse Mercator projection. Note that
        // Transverse Mercator is not the same as UTM; a scale factor is required to convert between them.
        //
        // Reference: Hoffmann-Wellenhof, B., Lichtenegger, H., and Collins, J.,
        // GPS: Theory and Practice, 3rd ed. New York: Springer-Verlag Wien, 1994.
        //
        // Inputs:
        //   phi - Latitude of the point, in radians.
        //   lambda - Longitude of the point, in radians.
        //   lambda0 - Longitude of the central meridian to be used, in radians.
        // Returns:
        //   CoordXZ containing the x and y coordinates of the computed point.
        //
        private CoordXZ MapLatLonToXY(double phi, double lambda1, double lambda0)
        {
            // Precalculate ep2
            double ep2 = (Math.Pow(SMa, 2.0) - Math.Pow(SMb, 2.0)) / Math.Pow(SMb, 2.0);

            // Precalculate nu2
            double nu2 = ep2 * Math.Pow(Math.Cos(phi), 2.0);

            // Precalculate N
            double N = Math.Pow(SMa, 2.0) / (SMb * Math.Sqrt(1 + nu2));

            // Precalculate t
            double t = Math.Tan(phi);
            double t2 = t * t;
            double _ = t2 * t2 * t2 - Math.Pow(t, 6.0);

            // Precalculate l
            double l = lambda1 - lambda0;

            // Precalculate coefficients for l**n in the equations below so a normal human being can read the
            // expressions for easting and northing. l**1 and l**2 have coefficients of 1.0
            double l3coef = 1.0 - t2 + nu2;

            double l4coef = 5.0 - t2 + 9 * nu2 + 4.0 * (nu2 * nu2);

            double l5coef = 5.0 - 18.0 * t2 + t2 * t2 + 14.0 * nu2 - 58.0 * t2 * nu2;

            double l6coef = 61.0 - 58.0 * t2 + t2 * t2 + 270.0 * nu2 - 330.0 * t2 * nu2;

            double l7coef = 61.0 - 479.0 * t2 + 179.0 * (t2 * t2) - t2 * t2 * t2;

            double l8coef = 1385.0 - 3111.0 * t2 + 543.0 * (t2 * t2) - t2 * t2 * t2;

            return new CoordXZ()
            {
                // Calculate easting (x)
                X = N * Math.Cos(phi) * l
                  + N / 6.0 * Math.Pow(Math.Cos(phi), 3.0) * l3coef * Math.Pow(l, 3.0)
                  + N / 120.0 * Math.Pow(Math.Cos(phi), 5.0) * l5coef * Math.Pow(l, 5.0)
                  + N / 5040.0 * Math.Pow(Math.Cos(phi), 7.0) * l7coef * Math.Pow(l, 7.0),

                // Calculate northing (z)
                Z = ArcLengthOfMeridian(phi)
                  + t / 2.0 * N * Math.Pow(Math.Cos(phi), 2.0) * Math.Pow(l, 2.0)
                  + t / 24.0 * N * Math.Pow(Math.Cos(phi), 4.0) * l4coef * Math.Pow(l, 4.0)
                  + t / 720.0 * N * Math.Pow(Math.Cos(phi), 6.0) * l6coef * Math.Pow(l, 6.0)
                  + t / 40320.0 * N * Math.Pow(Math.Cos(phi), 8.0) * l8coef * Math.Pow(l, 8.0)
            };
        }

        // Converts x and y coordinates in the Transverse Mercator projection to a latitude/longitude pair. Note that
        // Transverse Mercator is not the same as UTM; a scale factor is required to convert between them.
        //
        // Reference: Hoffmann-Wellenhof, B., Lichtenegger, H., and Collins, J.,
        // GPS: Theory and Practice, 3rd ed. New York: Springer-Verlag Wien, 1994.
        //
        // Inputs:
        //   x - The easting of the point, in meters.
        //   y - The northing of the point, in meters.
        //   lambda0 - Longitude of the central meridian to be used, in radians.
        //
        // Outputs:
        //   CoordLL containing the latitude and longitude in radians.
        //
        // Remarks:
        //   The local variables Nf, nuf2, tf, and tf2 serve the same purpose as N, nu2, t, and t2 in MapLatLonToXY,
        //   but they are computed with respect to the footpoint latitude phif.
        //
        //   x1frac, x2frac, x2poly, x3poly, etc. are to enhance readability and to optimize computations.
        //
        private CoordLL MapXYToLatLon(double x, double y, double lambda0)
        {
            // Get the value of phif, the footpoint latitude.
            double phif = FootpointLatitude(y);

            // Precalculate ep2
            double ep2 = (Math.Pow(SMa, 2.0) - Math.Pow(SMb, 2.0)) / Math.Pow(SMb, 2.0);

            // Precalculate cos (phif)
            double cf = Math.Cos(phif);

            // Precalculate nuf2
            double nuf2 = ep2 * Math.Pow(cf, 2.0);

            // Precalculate Nf and initialize Nfpow
            double Nf = Math.Pow(SMa, 2.0) / (SMb * Math.Sqrt(1 + nuf2));
            double Nfpow = Nf;

            // Precalculate tf
            double tf = Math.Tan(phif);
            double tf2 = tf * tf;
            double tf4 = tf2 * tf2;

            // Precalculate fractional coefficients for x**n in the equations below to simplify the expressions
            // for latitude and longitude.
            double x1frac = 1.0 / (Nfpow * cf);

            Nfpow *= Nf; // now equals Nf**2)
            double x2frac = tf / (2.0 * Nfpow);

            Nfpow *= Nf; // now equals Nf**3)
            double x3frac = 1.0 / (6.0 * Nfpow * cf);

            Nfpow *= Nf; // now equals Nf**4)
            double x4frac = tf / (24.0 * Nfpow);

            Nfpow *= Nf; // now equals Nf**5)
            double x5frac = 1.0 / (120.0 * Nfpow * cf);

            Nfpow *= Nf; // now equals Nf**6)
            double x6frac = tf / (720.0 * Nfpow);

            Nfpow *= Nf; // now equals Nf**7)
            double x7frac = 1.0 / (5040.0 * Nfpow * cf);

            Nfpow *= Nf; // now equals Nf**8)
            double x8frac = tf / (40320.0 * Nfpow);

            // Precalculate polynomial coefficients for x**n. x**1 does not have a polynomial coefficient.
            double x2poly = -1.0 - nuf2;

            double x3poly = -1.0 - 2 * tf2 - nuf2;

            double x4poly = 5.0 + 3.0 * tf2 + 6.0 * nuf2 - 6.0 * tf2 * nuf2 - 3.0 * (nuf2 * nuf2) - 9.0 * tf2 * (nuf2 * nuf2);

            double x5poly = 5.0 + 28.0 * tf2 + 24.0 * tf4 + 6.0 * nuf2 + 8.0 * tf2 * nuf2;

            double x6poly = -61.0 - 90.0 * tf2 - 45.0 * tf4 - 107.0 * nuf2 + 162.0 * tf2 * nuf2;

            double x7poly = -61.0 - 662.0 * tf2 - 1320.0 * tf4 - 720.0 * (tf4 * tf2);

            double x8poly = 1385.0 + 3633.0 * tf2 + 4095.0 * tf4 + 1575.0 * (tf4 * tf2);

            return new CoordLL()
            {
                // Calculate latitude
                Lat = phif + x2frac * x2poly * (x * x)
                    + x4frac * x4poly * Math.Pow(x, 4.0)
                    + x6frac * x6poly * Math.Pow(x, 6.0)
                    + x8frac * x8poly * Math.Pow(x, 8.0),

                // Calculate longitude
                Lon = lambda0 + x1frac * x
                   + x3frac * x3poly * Math.Pow(x, 3.0)
                   + x5frac * x5poly * Math.Pow(x, 5.0)
                   + x7frac * x7poly * Math.Pow(x, 7.0)
            };
        }

        // Converts a latitude/longitude pair to x and y coordinates in the Universal Transverse Mercator projection.
        //
        // Inputs:
        //   lat - Latitude of the point, in radians.
        //   lon - Longitude of the point, in radians.
        //   zone - UTM zone to be used for calculating values for x and y.
        //
        //   If zone is less than 1 or greater than 60, function determines the appropriate zone from the value of lon.
        //
        // Outputs:
        //   CoordXZ 2-element array where the UTM x and y values will be stored.
        //
        private CoordXZ LatLonToUTMXY(double lat, double lon, int zone)
        {
            CoordXZ xz = MapLatLonToXY(lat, lon, UTMCentralMeridian(zone));

            // Adjust easting and northing for UTM system.
            xz.X *= UTMScaleFactor;
            xz.Z *= UTMScaleFactor;
            if (xz.Z < 0.0)
            {
                xz.Z += 10000000.0;
            }
            // TODO: original also returns zone
            return xz;
        }

        // Converts x and y coordinates in the Universal Transverse Mercator projection to a latitude/longitude pair.
        //
        // Inputs:
        //	 x - The easting of the point, in meters.
        //   y - The northing of the point, in meters.
        //   zone - The UTM zone in which the point lies.
        //	 southhemi - True if the point is in the southern hemisphere; false otherwise.
        //
        // Outputs:
        //   CoordLL containing the latitude and longitude of the point, in radians.
        //
        private CoordLL UTMXYToLatLon(double x, double y, int zone, bool isSouthHemi)
        {
            // If in southern hemisphere, adjust y accordingly.
            if (isSouthHemi)
            {
                y -= 10000000.0;
            }
            x /= UTMScaleFactor;
            y /= UTMScaleFactor;

            return MapXYToLatLon(x, y, UTMCentralMeridian(zone));
        }
    }
}