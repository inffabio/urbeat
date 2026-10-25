const createMap = () => ({
  setView: jest.fn().mockReturnThis(),
  fitBounds: jest.fn().mockReturnThis(),
  removeLayer: jest.fn().mockReturnThis(),
  on: jest.fn().mockReturnThis(),
  remove: jest.fn(),
});

const createTileLayer = () => ({
  addTo: jest.fn().mockReturnThis(),
  on: jest.fn().mockReturnThis(),
});

const L = {
  map: jest.fn().mockImplementation(() => createMap()),
  tileLayer: jest.fn().mockImplementation(() => createTileLayer()),
  marker: jest.fn().mockReturnValue({
    addTo: jest.fn().mockReturnThis(),
    bindPopup: jest.fn().mockReturnThis(),
    getLatLng: jest.fn().mockReturnValue({ lat: -23.55, lng: -46.63 }),
    remove: jest.fn(),
  }),
  polyline: jest.fn().mockReturnValue({
    addTo: jest.fn().mockReturnThis(),
    getBounds: jest.fn().mockReturnValue({}),
    remove: jest.fn(),
  }),
  latLngBounds: jest.fn().mockReturnValue({}),
  divIcon: jest.fn().mockReturnValue({}),
  circle: jest.fn().mockReturnValue({ addTo: jest.fn() }),
  circleMarker: jest.fn().mockReturnValue({
    addTo: jest.fn().mockReturnThis(),
    bindPopup: jest.fn().mockReturnThis(),
  }),
  featureGroup: jest.fn().mockReturnValue({
    getBounds: jest.fn().mockReturnValue({}),
  }),
};

export default L;
