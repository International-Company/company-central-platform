import { getTranslations } from 'next-intl/server';
import { PageHeader } from '@/components/shared/page-header';

export default async function DashboardPage() {
  const t = await getTranslations('nav');

  return <PageHeader title={t('dashboard')} />;
}
