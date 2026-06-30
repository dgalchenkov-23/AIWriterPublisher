import type { ButtonHTMLAttributes, ReactNode } from 'react';

interface ShimmerButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
  loading?: boolean;
  size?: 'md' | 'lg';
}

export default function ShimmerButton({
  children,
  loading = false,
  size = 'lg',
  className = '',
  disabled,
  ...props
}: ShimmerButtonProps) {
  const sizeClass = size === 'lg' ? 'shimmer-btn--lg' : 'shimmer-btn--md';

  return (
    <button
      type="button"
      disabled={disabled || loading}
      className={`shimmer-btn ${sizeClass} ${loading ? 'shimmer-btn--loading' : ''} ${className}`}
      {...props}
    >
      <span className="shimmer-btn__glow" aria-hidden />
      <span className="shimmer-btn__shimmer" aria-hidden />
      <span className="shimmer-btn__label">{children}</span>
    </button>
  );
}
