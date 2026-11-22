import { describe, expect, it } from 'vitest';
import { buildMemberOptions, resolveMemberOption } from './memberOptions';

describe('buildMemberOptions', () => {
  it('labels a member by nickname', () => {
    expect(buildMemberOptions([{ userId: 'a1', nickname: 'Ada' }])).toEqual([
      { userId: 'a1', nickname: 'Ada', label: 'Ada' },
    ]);
  });

  it('tells members sharing a nickname apart by their user id, ignoring case', () => {
    const labels = buildMemberOptions([
      { userId: 'a1', nickname: 'Ada' },
      { userId: 'a2', nickname: 'ada' },
      { userId: 'b1', nickname: 'Bob' },
    ]).map((option) => option.label);

    expect(labels).toEqual(['Ada (a1)', 'ada (a2)', 'Bob']);
  });
});

describe('resolveMemberOption', () => {
  const options = buildMemberOptions([
    { userId: 'a1', nickname: 'Ada' },
    { userId: 'a2', nickname: 'Ada' },
    { userId: 'b1', nickname: 'Bob' },
  ]);

  it('finds a member by label, ignoring case and spaces', () => {
    expect(resolveMemberOption(options, '  bob ')?.userId).toBe('b1');
    expect(resolveMemberOption(options, 'ada (a2)')?.userId).toBe('a2');
  });

  it('matches nobody for a shared nickname, a partial name or nothing', () => {
    expect(resolveMemberOption(options, 'Ada')).toBeNull();
    expect(resolveMemberOption(options, 'Bo')).toBeNull();
    expect(resolveMemberOption(options, '')).toBeNull();
  });
});
