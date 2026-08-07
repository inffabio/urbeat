const L = {
  map: jest.fn().mockReturnValue({
    setView: jest.fn().mockReturnThis(),
    remove: jest.fn(),
  }),
  tileLayer: jest.fn().mockReturnValue({ addTo: jest.fn() }),
  marker: jest.fn().mockReturnValue({
    addTo: jest.fn().mockReturnThis(),
    bindPopup: jest.fn().mockReturnThis(),
    getLatLng: jest.fn().mockReturnValue({ lat: -23.55, lng: -46.63 }),
  }),
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
