import { useCallback, useEffect, useState } from 'react';
import type { GeneratedCover } from '../../../types/manifest';

const EXIT_MS = 300;

interface GenerationLightboxProps {
  cover: GeneratedCover;
  onClose: () => void;
  onUseAsReference: (imageUrl: string) => void;
  onApproveExport: (cover: GeneratedCover) => void;
  isApproving?: boolean;
}

function downloadCover(cover: GeneratedCover) {
  const ext = cover.url.startsWith('data:image/png') ? 'png' : 'jpg';
  const filename = `cover-${cover.art_id.slice(0, 8)}.${ext}`;

  if (cover.url.startsWith('data:') || cover.url.startsWith('blob:')) {
    const link = document.createElement('a');
    link.href = cover.url;
    link.download = filename;
    link.click();
    return;
  }

  fetch(cover.url)
    .then((res) => res.blob())
    .then((blob) => {
      const blobUrl = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = blobUrl;
      link.download = filename;
      link.click();
      URL.revokeObjectURL(blobUrl);
    })
    .catch(() => window.open(cover.url, '_blank', 'noopener,noreferrer'));
}

export default function GenerationLightbox({
  cover,
  onClose,
  onUseAsReference,
  onApproveExport,
  isApproving = false,
}: GenerationLightboxProps) {
  const [isClosing, setIsClosing] = useState(false);

  const requestClose = useCallback(() => {
    setIsClosing((prev) => {
      if (prev) return prev;
      return true;
    });
  }, []);

  useEffect(() => {
    if (!isClosing) return;
    const timer = window.setTimeout(onClose, EXIT_MS);
    return () => window.clearTimeout(timer);
  }, [isClosing, onClose]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') requestClose();
    };
    window.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = '';
    };
  }, [requestClose]);

  const backdropClass = isClosing ? 'lightbox-backdrop--exit' : 'lightbox-backdrop--enter';
  const panelClass = isClosing ? 'lightbox-panel--exit' : 'lightbox-panel--enter';

  return (
    <div
      className={`fixed inset-0 z-50 flex items-center justify-center p-4 md:p-8 bg-black/80 backdrop-blur-sm ${backdropClass}`}
      role="dialog"
      aria-modal="true"
      aria-label="Просмотр генерации"
      onClick={requestClose}
    >
      <div
        className={`relative flex flex-col lg:flex-row gap-6 max-w-6xl w-full max-h-[92vh] ${panelClass}`}
        onClick={(e) => e.stopPropagation()}
      >
        <button
          type="button"
          onClick={requestClose}
          className="absolute -top-2 right-0 lg:right-auto lg:-left-2 lg:top-0 z-10 w-9 h-9 rounded-full bg-slate-800/90 border border-slate-600 text-slate-300 hover:text-white hover:bg-slate-700 transition-colors duration-200"
          aria-label="Закрыть"
        >
          ✕
        </button>

        <div className="flex-1 flex items-center justify-center min-h-0 rounded-xl overflow-hidden bg-slate-950/50 border border-slate-700/50 lightbox-image--enter">
          <img
            src={cover.url}
            alt="Генерация в полном разрешении"
            className="max-w-full max-h-[70vh] lg:max-h-[85vh] w-auto h-auto object-contain"
          />
        </div>

        <aside className="lg:w-72 shrink-0 flex flex-col gap-3">
          <div className="rounded-xl border border-slate-700/80 bg-slate-900/90 p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-2">
              Студийные действия
            </p>
            <p className="text-[10px] font-mono text-slate-600 truncate mb-4">{cover.art_id}</p>
            <p className="text-[11px] text-slate-500 mb-4">
              {new Date(cover.timestamp).toLocaleString('ru-RU')}
            </p>

            <div className="flex flex-col gap-2">
              <button
                type="button"
                onClick={() => downloadCover(cover)}
                className="w-full px-4 py-2.5 rounded-lg text-sm font-semibold text-left bg-slate-800 hover:bg-slate-700 border border-slate-600 transition-colors duration-200"
              >
                💾 Скачать оригинал
              </button>
              <button
                type="button"
                onClick={() => {
                  onUseAsReference(cover.url);
                  requestClose();
                }}
                className="w-full px-4 py-2.5 rounded-lg text-sm font-semibold text-left bg-indigo-600/20 hover:bg-indigo-600/35 border border-indigo-500/40 text-indigo-100 transition-colors duration-200"
              >
                📎 Использовать как референс
              </button>
              <button
                type="button"
                disabled={isApproving}
                onClick={() => onApproveExport(cover)}
                className="w-full px-4 py-3 rounded-lg text-sm font-bold text-left bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white shadow-lg shadow-emerald-900/40 disabled:opacity-50 disabled:cursor-not-allowed transition-all duration-200"
              >
                {isApproving ? 'Отправка…' : '✅ Утвердить в печать / Экспорт'}
              </button>
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
}
