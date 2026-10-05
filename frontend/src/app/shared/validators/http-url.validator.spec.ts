import { FormControl } from '@angular/forms';
import { httpUrlValidator } from './http-url.validator';

describe('httpUrlValidator', () => {
  const check = (value: string) => httpUrlValidator(new FormControl(value));

  it('accepts empty and absolute http(s) URLs', () => {
    expect(check('')).toBeNull();
    expect(check('https://brightline.example/demo?x=1')).toBeNull();
    expect(check('http://localhost:4200')).toBeNull();
  });

  it('rejects relative, non-http and credential-bearing URLs', () => {
    for (const bad of ['brightline.example', '/path', 'ftp://host/file', 'javascript:alert(1)', 'https://u:p@host.example']) {
      expect(check(bad)).toEqual({ httpUrl: true });
    }
  });
});
