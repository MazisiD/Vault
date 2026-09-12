/**
 * Frontend configuration. The presentation layer only needs to know where the
 * backend API and the Supabase project live - it holds no business rules.
 *
 * Get the Supabase values from your project's dashboard: Settings → API →
 * Project URL and Project API keys → anon/public.
 */
export const environment = {
  apiBaseUrl: 'http://localhost:5080',
  supabaseUrl: 'https://zlxmgthiaeabsehvrvsi.supabase.co',
  supabaseAnonKey: 'sb_publishable_Ot6daQf4u_ouPoZP3BxTBw_RRps-sx-',
};
