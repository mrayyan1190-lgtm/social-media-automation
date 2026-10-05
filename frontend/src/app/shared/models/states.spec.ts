import { DELIVERY_LABELS, deliveryTone } from './states';

describe('delivery state presentation', () => {
  it('never presents a simulation as a publication', () => {
    expect(deliveryTone('SIMULATED')).toBe('simulated');
    expect(deliveryTone('PUBLISHED')).toBe('success');
    expect(DELIVERY_LABELS['SIMULATED']).toContain('not posted');
  });

  it('marks every state that needs a person as danger', () => {
    for (const state of ['FAILED', 'ACTION_REQUIRED', 'RECONCILIATION_REQUIRED']) expect(deliveryTone(state)).toBe('danger');
  });
});
