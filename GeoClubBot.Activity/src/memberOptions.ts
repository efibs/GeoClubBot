import type { AdminMemberDto } from './types';

/** A member as the picker offers it. */
export interface MemberOption {
  userId: string;
  nickname: string;
  /** What the admin sees and types: the nickname, or "nickname (userId)" when it is shared. */
  label: string;
}

/**
 * The picker's options. GeoGuessr nicknames are not unique, so members sharing one (ignoring case)
 * are told apart by their user id — the same labels as the bot's Discord autocomplete (#139).
 */
export function buildMemberOptions(members: readonly AdminMemberDto[]): MemberOption[] {
  const counts = new Map<string, number>();
  for (const member of members) {
    const key = member.nickname.toLowerCase();
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  return members.map((member) => ({
    userId: member.userId,
    nickname: member.nickname,
    label:
      (counts.get(member.nickname.toLowerCase()) ?? 0) > 1
        ? `${member.nickname} (${member.userId})`
        : member.nickname,
  }));
}

/**
 * The option whose label was typed or picked (ignoring case and surrounding spaces), or null.
 * A nickname that several members share matches none of them: only its "(userId)" labels do.
 */
export function resolveMemberOption(
  options: readonly MemberOption[],
  text: string,
): MemberOption | null {
  const wanted = text.trim().toLowerCase();
  if (!wanted) {
    return null;
  }
  return options.find((option) => option.label.toLowerCase() === wanted) ?? null;
}
