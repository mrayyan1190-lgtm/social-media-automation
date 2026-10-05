import { asUtc, dayKeyInZone, formatOffset, offsetsFor, toUtc } from './time-zones';

describe('time zone helpers', () => {
  it('finds one offset for an ordinary time', () => {
    expect(offsetsFor('2027-07-01', '09:30', 'America/New_York')).toEqual([-240]);
    expect(toUtc('2027-07-01', '09:30', -240).toISOString()).toBe('2027-07-01T13:30:00.000Z');
  });

  it('finds no offset for a time skipped by the spring-forward jump', () => {
    expect(offsetsFor('2027-03-14', '02:30', 'America/New_York')).toEqual([]);
  });

  it('finds both offsets for a time repeated when clocks go back, earlier occurrence first', () => {
    expect(offsetsFor('2027-11-07', '01:30', 'America/New_York')).toEqual([-240, -300]);
    expect(offsetsFor('2027-10-31', '01:30', 'Europe/London')).toEqual([60, 0]);
  });

  it('formats offsets', () => {
    expect(formatOffset(-300)).toBe('UTC-05:00');
    expect(formatOffset(330)).toBe('UTC+05:30');
  });

  it('groups a UTC instant into the calendar day of the workspace zone', () => {
    expect(dayKeyInZone('2027-07-01T02:30:00', 'America/New_York')).toBe('2027-06-30');
    expect(dayKeyInZone('2027-07-01T02:30:00', 'Asia/Karachi')).toBe('2027-07-01');
    expect(asUtc('2027-07-01T02:30:00')).toBe('2027-07-01T02:30:00Z');
    expect(asUtc('2027-07-01T02:30:00Z')).toBe('2027-07-01T02:30:00Z');
  });
});
