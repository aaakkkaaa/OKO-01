namespace Mapbox.Unity.Map
{
    //using System;
	using Mapbox.Utils;
	using Mapbox.Unity.Utilities;
	using Mapbox.Map;
	using UnityEngine;

	public class Sc02MapAtWorldScaleAndSpecificLocation : AbstractMap
	{
		[SerializeField]
		bool _useRelativeScale;

		public override void Initialize(Vector2d latLon, int zoom)
		{
			_worldHeightFixed = false;
			_centerLatitudeLongitude = latLon;
			_zoom = zoom;

			var referenceTileRect = Conversions.TileBounds(TileCover.CoordinateToTileId(_centerLatitudeLongitude, _zoom));
			_centerMercator = referenceTileRect.Center;

			_worldRelativeScale = _useRelativeScale ? Mathf.Cos(Mathf.Deg2Rad * (float)_centerLatitudeLongitude.x) : 1f;

            // The magic line.
            _root.localPosition = -Conversions.GeoToWorldPosition(_centerLatitudeLongitude.x, _centerLatitudeLongitude.y, _centerMercator, _worldRelativeScale).ToVector3xz();

            //print("_centerLatitudeLongitude.x = " + _centerLatitudeLongitude.x + "_centerLatitudeLongitude.y = " + _centerLatitudeLongitude.y);
            //print("_centerMercator = " + _centerMercator + "_worldRelativeScale = " + _worldRelativeScale);
            //print("_root.localPosition = " + _root.localPosition);
            sFlightRadar myFlightRadar = GameObject.Find("Boss").GetComponent<sFlightRadar>();
            myFlightRadar.myWorldRelativeScale = _worldRelativeScale;
            myFlightRadar.myCenterMercator = _centerMercator;
            myFlightRadar.myPosShift = _root.localPosition;
            string[] myLatLonArray = _latitudeLongitudeString.Split(new char[] { ',', ' ' });
            myFlightRadar.myStartLatitude = myLatLonArray[0];
            myFlightRadar.myStartLongitude = myLatLonArray[1];

            print("myStartLatitude = " + myFlightRadar.myStartLatitude + "myStartLongitude = " + myFlightRadar.myStartLongitude);

            _mapVisualizer.Initialize(this, _fileSouce);
			_tileProvider.Initialize(this);

			SendInitialized();
		}
	}
}
