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
  'inline-flex items-center justify-center gap-2 rounded-sm border font-medium ' +
  'whitespace-nowrap transition-colors duration-100 ' +
  'disabled:cursor-not-allowed disabled:opacity-50';

const variants: Record<Variant, string> = {
  primary:
    'border-primary-700 bg-primary-700 text-text-on-primary ' +
    'hover:border-primary-900 hover:bg-primary-900',
  secondary:
    'border-border-strong bg-surface text-text ' +
    'hover:border-primary-700 hover:text-primary-900',

  // A text action. Underlined on hover rather than always: a table row with
  // two permanently underlined words in its last column reads as a web page
  // from another decade, and the colour already says it can be pressed.
  quiet:
    'border-transparent bg-transparent text-primary-700 ' +
    'hover:text-primary-900 hover:underline underline-offset-4',

  // The deepest blue, not red. White and blue only holds for destructive
  // actions too: what makes this one serious is the confirmation that names
  // what will happen, and a colour change is no substitute for reading it.
  danger:
    'border-primary-900 bg-primary-900 text-text-on-primary ' +
    'hover:border-text hover:bg-text',
};

const sizes: Record<Size, string> = {
  sm: 'h-8 px-3 text-sm',
  md: 'h-10 px-5 text-sm',
};

// No horizontal padding: a text action aligns with the text around it. Kept
// apart rather than overridden, because two padding utilities on one element
// resolve by stylesheet order, not by the order they are written in.
const quietSizes: Record<Size, string> = {
  sm: 'h-8 text-sm',
  md: 'h-10 text-sm',
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
      className={`${base} ${variants[variant]} ${variant === 'quiet' ? quietSizes[size] : sizes[size]} ${className}`}
      {...rest}
    >
      {busy && busyLabel ? busyLabel : children}
    </button>
  );
}
