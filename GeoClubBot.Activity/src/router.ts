import { createRouter, createWebHashHistory } from 'vue-router';
import { useSessionStore } from './stores/session';

/**
 * Hash history keeps routing entirely client-side, which is the safe choice inside the Discord
 * iframe: the activity is always booted at its root URL (deep links don't exist there) and hash
 * URLs survive the Discord asset proxy without server-side fallbacks.
 */
export const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', component: () => import('./views/OverviewView.vue') },
    { path: '/missions', component: () => import('./views/MissionsView.vue') },
    { path: '/me', component: () => import('./views/MeView.vue') },
    {
      path: '/admin',
      component: () => import('./views/admin/AdminLayout.vue'),
      children: [
        { path: '', redirect: '/admin/strikes' },
        { path: 'strikes', component: () => import('./views/admin/AdminStrikesView.vue') },
        { path: 'excuses', component: () => import('./views/admin/AdminExcusesView.vue') },
        { path: 'members', component: () => import('./views/admin/AdminMembersView.vue') },
        { path: 'linking', component: () => import('./views/admin/AdminLinkingView.vue') },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
});

router.beforeEach((to) => {
  if (to.path.startsWith('/admin')) {
    // Cosmetic gate only — every admin endpoint enforces the same policy server-side. This just
    // keeps non-admins from landing on a shell full of 403s.
    const session = useSessionStore();
    if (!session.isAdmin) {
      return { path: '/' };
    }
  }
  return true;
});
