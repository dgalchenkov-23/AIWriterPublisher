import type { ReactNode } from 'react';

interface MagicBorderProps {
  children: ReactNode;
  className?: string;
  innerClassName?: string;
  variant?: 'violet' | 'emerald' | 'amber';
}

const variantClass: Record<NonNullable<MagicBorderProps['variant']>, string> = {
  violet: 'magic-border--violet',
  emerald: 'magic-border--emerald',
  amber: 'magic-border--amber',
};

export default function MagicBorder({
  children,
  className = '',
  innerClassName = '',
  variant = 'violet',
}: MagicBorderProps) {
  return (
    <div className={`magic-border ${variantClass[variant]} ${className}`}>
      <div className={`magic-border__inner ${innerClassName}`}>{children}</div>
    </div>
  );
}
