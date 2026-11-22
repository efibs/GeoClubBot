import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import MemberPicker from './MemberPicker.vue';
import type { AdminMemberDto } from '../types';

const members: AdminMemberDto[] = [
  { userId: 'a1', nickname: 'Ada' },
  { userId: 'a2', nickname: 'Ada' },
  { userId: 'b1', nickname: 'Bob' },
];

function mountPicker(initial: string | null = null) {
  const wrapper = mount(MemberPicker, {
    props: {
      id: 'member',
      label: 'Member',
      members,
      modelValue: initial,
      'onUpdate:modelValue': (value: string | null) => wrapper.setProps({ modelValue: value }),
    },
  });
  return wrapper;
}

describe('MemberPicker', () => {
  it('offers every member, shared nicknames with their user id', () => {
    const labels = mountPicker()
      .findAll('option')
      .map((option) => option.attributes('value'));

    expect(labels).toEqual(['Ada (a1)', 'Ada (a2)', 'Bob']);
  });

  it('models the user id of the member whose label was picked', async () => {
    const wrapper = mountPicker();

    await wrapper.find('input').setValue('Ada (a2)');

    expect(wrapper.props('modelValue')).toBe('a2');
    expect(wrapper.find('[data-testid="member-unresolved"]').exists()).toBe(false);
  });

  it('models nothing, and says so, for a name no single member has', async () => {
    const wrapper = mountPicker();

    await wrapper.find('input').setValue('Ada');

    expect(wrapper.props('modelValue')).toBeNull();
    expect(wrapper.find('[data-testid="member-unresolved"]').exists()).toBe(true);
  });

  it('clears its text when the form clears the id', async () => {
    const wrapper = mountPicker();
    await wrapper.find('input').setValue('Bob');

    await wrapper.setProps({ modelValue: null });
    await nextTick();

    expect((wrapper.find('input').element as HTMLInputElement).value).toBe('');
  });
});
