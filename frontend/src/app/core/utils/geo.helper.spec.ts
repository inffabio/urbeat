import { formatDistanceKm, haversineDistanceKm, toGeoPoint } from './geo.helper';

describe('geo.helper', () => {
  describe('toGeoPoint', () => {
    it('returns a point for valid coordinates', () => {
      expect(toGeoPoint(-23.55052, -46.633308)).toEqual({ lat: -23.55052, lng: -46.633308 });
    });

    it.each([
      [undefined, -46.6],
      [null, -46.6],
      [-23.5, undefined],
      [Number.NaN, -46.6],
      [91, -46.6],
      [-23.5, 181],
      [Number.POSITIVE_INFINITY, 0],
    ])('rejects invalid pair (%p, %p)', (lat, lng) => {
      expect(toGeoPoint(lat as number | null | undefined, lng as number | null | undefined)).toBeNull();
    });
  });

  describe('haversineDistanceKm', () => {
    it('returns zero for the same point', () => {
      const point = { lat: -23.55, lng: -46.63 };
      expect(haversineDistanceKm(point, point)).toBe(0);
    });

    it('computes the known distance between Sao Paulo landmarks', () => {
      // Praca da Se -> Avenida Paulista (MASP), roughly 3.1 km in a straight line.
      const distance = haversineDistanceKm(
        { lat: -23.55052, lng: -46.633308 },
        { lat: -23.561414, lng: -46.655881 },
      );
      expect(distance).toBeGreaterThan(2.5);
      expect(distance).toBeLessThan(3.5);
    });
  });

  describe('formatDistanceKm', () => {
    it('formats short distances in meters', () => {
      expect(formatDistanceKm(0.42)).toBe('420 m');
      expect(formatDistanceKm(0)).toBe('1 m');
    });

    it('formats long distances in kilometres with pt-BR decimals', () => {
      expect(formatDistanceKm(3.44)).toBe('3,4 km');
      expect(formatDistanceKm(12)).toBe('12 km');
    });

    it('returns an empty string for unusable values', () => {
      expect(formatDistanceKm(Number.NaN)).toBe('');
      expect(formatDistanceKm(-1)).toBe('');
    });
  });
});
