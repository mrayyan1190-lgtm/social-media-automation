import { fromList, toList } from './list-text';

describe('list-text', () => {
  it('splits on newlines and commas and drops blanks', () => {
    expect(toList(' clear,\nconfident \n\n, practical ')).toEqual(['clear', 'confident', 'practical']);
  });

  it('round-trips a list', () => {
    expect(toList(fromList(['a', 'b']))).toEqual(['a', 'b']);
    expect(fromList(null)).toBe('');
  });
});
