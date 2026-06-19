import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import PeriodFilter from './PeriodFilter.vue';
import { depthOptions } from '../format';

describe('PeriodFilter', () => {
  it('marks the active option and emits the value on click', async () => {
    const wrapper = mount(PeriodFilter, {
      props: { modelValue: depthOptions[0].value, options: depthOptions },
    });

    expect(wrapper.find('.period-button.active').text()).toBe(depthOptions[0].label);

    await wrapper.find(`[data-testid="period-${depthOptions[1].value}"]`).trigger('click');

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([depthOptions[1].value]);
  });
});
