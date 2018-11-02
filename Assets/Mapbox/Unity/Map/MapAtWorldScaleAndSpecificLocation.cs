namespace Mapbox.Unity.Map
{
	using Mapbox.Utils;
	using Mapbox.Unity.Utilities;
	using Mapbox.Map;
	using UnityEngine;

	public class MapAtWorldScaleAndSpecificLocation : AbstractMap
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

            _mapVisualizer.Initialize(this, _fileSouce);
			_tileProvider.Initialize(this);

			SendInitialized();
		}
	}
}
