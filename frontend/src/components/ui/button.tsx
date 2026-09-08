import type { ButtonHTMLAttributes, ReactNode } from 'react';

/**
 * A button, and it says what it does.
 *
 * **Text, never an icon alone** (ARCHITECTURE.md §9.5). *Create User*, *Save*,
 * *Delete* — never a glyph the user has to interpret. An icon-only control is a
 * guessing game for a sighted user and a labelling problem for everyone else,
 * and it saves nothing but a few pixels.
 *
 * There is no `icon` prop. Adding one would make the wrong thing easy.
 */

type Variant = 'primary' | 'secondary' | 'quiet' | 'danger';
type Size = 'sm' | 'md';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;

  /**
   * Shown instead of the label while an action is in flight, and disables the
   * button. Prevents the double submit that creates two users.
   */
  busy?: boolean;
  busyLabel?: string;
  children: ReactNode;
}

const base =
  'inline-flex items-center justify-center rounded-md border font-medium ' +
  'transition-colors duration-100 disabled:cursor-not-allowed disabled:opacity-55';

const variants: Record<Variant, string> = {
  primary:
    'border-transparent bg-primary-600 text-text-on-primary ' +
    'hover:bg-primary-700',
  secondary:
    'border-border-strong bg-surface text-text ' +
    'hover:bg-surface-sunken',
  quiet:
    'border-transparent bg-transparent text-primary-700 underline ' +
    'underline-offset-2 hover:text-primary-900',

  // Destructive actions read as destructive in *words* as well as colour: the
  // label says Delete and a confirmation states what will be removed. Colour
  // alone fails a colour-blind reader and vanishes on paper (§9.8).
  danger:
    'border-transparent bg-danger text-white hover:brightness-95',
};

const sizes: Record<Size, string> = {
  sm: 'h-8 px-3 text-sm',
  md: 'h-10 px-4 text-sm',
};

export function Button({
  variant = 'secondary',
  size = 'md',
  busy = false,
  busyLabel,
  children,
  className = '',
  disabled,
  type = 'button',
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled === true || busy}
      // Announced to a screen reader, which otherwise has no way to know the
      // page is waiting on something.
      aria-busy={busy || undefined}
      className={`${base} ${variants[variant]} ${sizes[size]} ${className}`}
      {...rest}
    >
      {busy && busyLabel ? busyLabel : children}
    </button>
  );
}
