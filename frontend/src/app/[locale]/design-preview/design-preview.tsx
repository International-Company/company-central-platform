'use client';

import { AppShell } from '@/components/layout/app-shell';
import { ConfirmDialog } from '@/components/shared/confirm-dialog';
import { DataTable, EmptyState } from '@/components/shared/data-table';
import { FormDialog } from '@/components/shared/form-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge } from '@/components/shared/status-badge';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DashboardSummary } from '@/features/dashboard/dashboard-summary';
import type { Locale } from '@/i18n/config';

/**
 * Sample content for the design preview. Development only; see page.tsx.
 *
 * Written in both languages directly rather than through the catalogue: it is
 * not a screen anybody uses, and putting invented people into the message
 * files would ship them to production with the real text.
 */

type Row = {
  id: string;
  name: string;
  username: string;
  unit: string;
  status: 'active' | 'disabled' | 'locked' | 'pending';
  lastSignIn: string;
};

const text = {
  ar: {
    app: 'المنصة المركزية',
    nav: ['لوحة المتابعة', 'المهام', 'المستخدمون', 'الموظفون', 'الهيكل التنظيمي', 'الأدوار', 'سجل التدقيق'],
    title: 'المستخدمون',
    description: 'حسابات الدخول إلى المنصة وحالتها وآخر نشاط لها.',
    create: 'إنشاء مستخدم',
    columns: ['الاسم', 'اسم المستخدم', 'الوحدة', 'الحالة', 'آخر دخول'],
    status: { active: 'نشط', disabled: 'معطّل', locked: 'مقفل', pending: 'بانتظار التفعيل' },
    edit: 'تعديل',
    disable: 'تعطيل',
    noResults: 'لا توجد نتائج',
    noResultsDescription: 'لا يوجد مستخدمون يطابقون البحث.',
    actions: 'الإجراءات',
    showing: (from: number, to: number, total: number) => `عرض ${from} إلى ${to} من ${total}`,
    previous: 'السابق',
    next: 'التالي',
    formTitle: 'بيانات الحساب',
    username: 'اسم المستخدم',
    email: 'البريد الإلكتروني',
    required: 'مطلوب',
    hint: 'يُستخدم لإرسال إشعارات الأمان.',
    error: 'البريد الإلكتروني مستخدم في حساب آخر.',
    save: 'حفظ',
    cancel: 'إلغاء',
    messageError: 'تعذّر حفظ التغييرات. تحقق من الحقول المشار إليها.',
    messageSuccess: 'تم حفظ التغييرات.',
    messageInfo: 'تُطبَّق التغييرات على الجلسات الجديدة فقط.',
    buttons: 'الأزرار',
    messages: 'الرسائل',
    form: 'النموذج',
    empty: 'الحالة الفارغة',
    confirmTitle: 'تعطيل الحساب',
    confirmText: 'لن يتمكن هذا المستخدم من الدخول حتى يُعاد تفعيل الحساب.',
    danger: 'تعطيل',
    signOut: 'تسجيل الخروج',
    language: 'اللغة',
    menu: 'القائمة',
    close: 'إغلاق',
    skip: 'تخطٍّ إلى المحتوى',
    showSection: 'إظهار',
    hideSection: 'إخفاء',
    mainNav: 'التنقل الرئيسي',
    dashboardDescription: 'أين تقف المنصة الآن.',
    nextSteps: 'الخطوات التالية',
    unavailable: 'غير متاح',
    unavailableHint: 'لا تملك صلاحية قراءة هذا الرقم.',
    figures: ['المستخدمون', 'الموظفون', 'الوحدات التنظيمية', 'الأدوار'],
    steps: ['أنشئ الهيكل التنظيمي. لا يمكن إنشاء موظف قبل وجود وحدة.', 'أضف الموظفين إلى وحداتهم.', 'فعّل التحقّق بخطوتين. بدونه لا يمكنك منح الأدوار.', 'أنشئ حسابات المستخدمين وامنحهم الأدوار.'],
    sections: ['الأشخاص والتنظيم', 'الصلاحيات والأمان'],
  },
  en: {
    app: 'Central Platform',
    nav: ['Dashboard', 'Tasks', 'Users', 'Employees', 'Organization', 'Roles', 'Audit log'],
    title: 'Users',
    description: 'Accounts that can sign in to the Platform, their state and last activity.',
    create: 'Create user',
    columns: ['Name', 'Username', 'Unit', 'Status', 'Last sign-in'],
    status: { active: 'Active', disabled: 'Disabled', locked: 'Locked', pending: 'Pending activation' },
    edit: 'Edit',
    disable: 'Disable',
    noResults: 'No results',
    noResultsDescription: 'No users match the search.',
    actions: 'Actions',
    showing: (from: number, to: number, total: number) => `Showing ${from} to ${to} of ${total}`,
    previous: 'Previous',
    next: 'Next',
    formTitle: 'Account details',
    username: 'Username',
    email: 'Email',
    required: 'Required',
    hint: 'Used to send security notifications.',
    error: 'This email is already used by another account.',
    save: 'Save',
    cancel: 'Cancel',
    messageError: 'The changes could not be saved. Check the fields marked below.',
    messageSuccess: 'The changes were saved.',
    messageInfo: 'Changes apply to new sessions only.',
    buttons: 'Buttons',
    messages: 'Messages',
    form: 'Form',
    empty: 'Empty state',
    confirmTitle: 'Disable account',
    confirmText: 'This user will not be able to sign in until the account is enabled again.',
    danger: 'Disable',
    signOut: 'Sign out',
    language: 'Language',
    menu: 'Menu',
    close: 'Close',
    skip: 'Skip to content',
    showSection: 'Show',
    hideSection: 'Hide',
    mainNav: 'Main navigation',
    dashboardDescription: 'Where the Platform stands.',
    nextSteps: 'Next steps',
    unavailable: 'Not available',
    unavailableHint: 'You do not hold the permission to read this figure.',
    figures: ['Users', 'Employees', 'Organizational units', 'Roles'],
    steps: ['Build the organizational structure. No employee can exist before a unit does.', 'Add employees to their units.', 'Turn on two-factor authentication. Without it you cannot grant roles.', 'Create user accounts and grant them roles.'],
    sections: ['People and organization', 'Access and security'],
  },
} as const;

const rows: Record<Locale, Row[]> = {
  ar: [
    { id: '1', name: 'أحمد الحربي', username: 'a.alharbi', unit: 'الإدارة المالية', status: 'active', lastSignIn: '2026-09-14 09:12' },
    { id: '2', name: 'سارة القحطاني', username: 's.alqahtani', unit: 'الموارد البشرية', status: 'active', lastSignIn: '2026-09-14 08:47' },
    { id: '3', name: 'خالد المطيري', username: 'k.almutairi', unit: 'المشتريات', status: 'locked', lastSignIn: '2026-09-11 16:03' },
    { id: '4', name: 'نورة الشهري', username: 'n.alshehri', unit: 'تقنية المعلومات', status: 'pending', lastSignIn: '' },
    { id: '5', name: 'فيصل الدوسري', username: 'f.aldosari', unit: 'الشؤون القانونية', status: 'disabled', lastSignIn: '2026-08-02 11:30' },
    { id: '6', name: 'ريم العتيبي', username: 'r.alotaibi', unit: 'الإدارة المالية', status: 'active', lastSignIn: '2026-09-13 14:21' },
  ],
  en: [
    { id: '1', name: 'Ahmed Al-Harbi', username: 'a.alharbi', unit: 'Finance', status: 'active', lastSignIn: '2026-09-14 09:12' },
    { id: '2', name: 'Sara Al-Qahtani', username: 's.alqahtani', unit: 'Human Resources', status: 'active', lastSignIn: '2026-09-14 08:47' },
    { id: '3', name: 'Khalid Al-Mutairi', username: 'k.almutairi', unit: 'Procurement', status: 'locked', lastSignIn: '2026-09-11 16:03' },
    { id: '4', name: 'Noura Al-Shehri', username: 'n.alshehri', unit: 'Information Technology', status: 'pending', lastSignIn: '' },
    { id: '5', name: 'Faisal Al-Dosari', username: 'f.aldosari', unit: 'Legal', status: 'disabled', lastSignIn: '2026-08-02 11:30' },
    { id: '6', name: 'Reem Al-Otaibi', username: 'r.alotaibi', unit: 'Finance', status: 'active', lastSignIn: '2026-09-13 14:21' },
  ],
};

const tone = { active: 'success', disabled: 'neutral', locked: 'danger', pending: 'warning' } as const;

export function DesignPreview({
  locale,
  dialog,
  view,
}: {
  locale: Locale;
  dialog: string | null;
  view: string | null;
}) {
  const t = text[locale];

  return (
    <AppShell
      locale={locale}
      navigation={t.nav.map((label, index) => ({
        href: index === 2 ? '/design-preview' : `/preview-${index}`,
        label,
        section: index < 2 ? undefined : index < 5 ? t.sections[0] : t.sections[1],
      }))}
      labels={{
        appName: t.app,
        mainNavigation: t.mainNav,
        openMenu: t.menu,
        closeMenu: t.close,
        signOut: t.signOut,
        language: t.language,
        skipToContent: t.skip,
        showSection: t.showSection,
        hideSection: t.hideSection,
      }}
    >
      {view === 'dashboard' ? (
        <>
          <PageHeader title={t.nav[0] ?? ''} description={t.dashboardDescription} />
          <DashboardSummary
            heading={t.nav[0] ?? ''}
            nextStepsHeading={t.nextSteps}
            unavailable={t.unavailable}
            unavailableHint={t.unavailableHint}
            figures={t.figures.map((label, index) => ({
              href: `/preview-figure-${index}`,
              label,
              value: index === 3 ? null : ['248', '1,312', '37'][index] ?? null,
            }))}
            steps={t.steps.map((step, index) => ({
              href: `/preview-step-${index}`,
              text: step,
              number: String(index + 1),
            }))}
          />
        </>
      ) : null}

      <div hidden={view === 'dashboard'}>
      <PageHeader
        title={t.title}
        description={t.description}
        action={<Button variant="primary">{t.create}</Button>}
      />

      <DataTable
        caption={t.title}
        columns={[
          { key: 'name', header: t.columns[0], render: (row: Row) => row.name, sortable: true },
          { key: 'username', header: t.columns[1], render: (row: Row) => row.username },
          { key: 'unit', header: t.columns[2], render: (row: Row) => row.unit, secondary: true },
          {
            key: 'status',
            header: t.columns[3],
            render: (row: Row) => <StatusBadge tone={tone[row.status]}>{t.status[row.status]}</StatusBadge>,
          },
          { key: 'last', header: t.columns[4], render: (row: Row) => row.lastSignIn, numeric: true },
        ]}
        rows={rows[locale]}
        rowKey={(row) => row.id}
        sort={{ key: 'name', direction: 'asc' }}
        onSortChange={() => undefined}
        rowActions={() => (
          <>
            <Button variant="quiet" size="sm">{t.edit}</Button>
            <Button variant="quiet" size="sm">{t.disable}</Button>
          </>
        )}
        labels={{
          noResults: t.noResults,
          noResultsDescription: t.noResultsDescription,
          sortAscending: locale === 'ar' ? 'تصاعدي' : 'Ascending',
          sortDescending: locale === 'ar' ? 'تنازلي' : 'Descending',
          actions: t.actions,
        }}
      />

      <Pagination
        page={1}
        pageSize={6}
        totalItems={48}
        onPageChange={() => undefined}
        labels={{
          showing: ({ from, to, total }) => t.showing(from, to, total),
          previous: t.previous,
          next: t.next,
        }}
      />

      <div className="mt-10 grid gap-10 lg:grid-cols-2">
        <section>
          <h2 className="mb-3 text-sm font-semibold text-text">{t.form}</h2>

          <div className="flex flex-col gap-4">
            <Field label={t.username} defaultValue="a.alharbi" required requiredLabel={t.required} />
            <Field label={t.email} defaultValue="ahmed@example.com" hint={t.hint} error={t.error} required requiredLabel={t.required} />
          </div>
        </section>

        <section>
          <h2 className="mb-3 text-sm font-semibold text-text">{t.messages}</h2>

          <div className="flex flex-col gap-3">
            <FormMessage tone="error">{t.messageError}</FormMessage>
            <FormMessage tone="success">{t.messageSuccess}</FormMessage>
            <FormMessage tone="info">{t.messageInfo}</FormMessage>
          </div>

          <h2 className="mb-3 mt-8 text-sm font-semibold text-text">{t.buttons}</h2>

          <div className="flex flex-wrap gap-3">
            <Button variant="primary">{t.save}</Button>
            <Button variant="secondary">{t.cancel}</Button>
            <Button variant="quiet">{t.edit}</Button>
            <Button variant="danger">{t.danger}</Button>
            <Button variant="primary" disabled>{t.save}</Button>
          </div>
        </section>
      </div>

      <section className="mt-10">
        <h2 className="mb-3 text-sm font-semibold text-text">{t.empty}</h2>
        <EmptyState title={t.noResults} description={t.noResultsDescription} />
      </section>

      </div>

      <FormDialog
        open={dialog === 'form'}
        title={t.formTitle}
        submitLabel={t.save}
        cancelLabel={t.cancel}
        error={t.messageError}
        onSubmit={() => undefined}
        onCancel={() => undefined}
      >
        <Field label={t.username} defaultValue="a.alharbi" required requiredLabel={t.required} />
        <Field label={t.email} defaultValue="ahmed@example.com" error={t.error} />
      </FormDialog>

      <ConfirmDialog
        open={dialog === 'confirm'}
        title={t.confirmTitle}
        description={t.confirmText}
        confirmLabel={t.danger}
        cancelLabel={t.cancel}
        destructive
        onConfirm={() => undefined}
        onCancel={() => undefined}
      />
    </AppShell>
  );
}
