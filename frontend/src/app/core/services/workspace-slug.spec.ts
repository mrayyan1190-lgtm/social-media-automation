import { Membership } from '../auth/auth.models';
import { workspaceSlug } from './workspace.service';

const m = (organizationId: string, organizationName: string): Membership => ({ organizationId, organizationName, role: 'Admin' });

describe('workspaceSlug', () => {
  it('slugifies the workspace name', () => {
    const a = m('aaaaaaaa-1', 'Centaiva  Inc.');
    expect(workspaceSlug(a, [a])).toBe('centaiva-inc');
  });

  it('adds an id suffix when two of the user\'s workspaces share a name', () => {
    const a = m('aaaaaaaa-1', 'Acme');
    const b = m('bbbbbbbb-2', 'ACME');
    expect(workspaceSlug(a, [a, b])).toBe('acme-aaaaaaaa');
    expect(workspaceSlug(b, [a, b])).toBe('acme-bbbbbbbb');
  });

  it('falls back to the id when the name has no URL characters', () => {
    const a = m('cccccccc-3', '!!!');
    expect(workspaceSlug(a, [a])).toBe('cccccccc');
  });
});
