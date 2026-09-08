import { redirect } from 'next/navigation';

/**
 * The locale root sends people to the portal. Whether they get there is the
 * portal layout's decision, which is where the session is checked.
 */
export default async function LocaleRoot({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  redirect(`/${locale}/dashboard`);
}
