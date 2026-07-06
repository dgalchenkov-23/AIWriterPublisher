import type { AppMode } from '../types/manifest';

interface SidebarProps {
  activeMode: AppMode;
  onModeChange: (mode: AppMode) => void;
}

export default function Sidebar({ activeMode, onModeChange }: SidebarProps) {
  const menuItems: { mode: AppMode; label: string; icon: string; desc: string }[] = [
    { mode: 'Brainstorm', label: 'Brainstorm', icon: '🧠', desc: 'Поиск идей и стилей' },
    { mode: 'Self-Prompt', label: 'Self-Prompt', icon: '⚙️', desc: 'Инженерный пульт' },
    { mode: 'SceneMaker', label: 'Scene Maker', icon: '📖', desc: 'Генерация из глав' },
    { mode: 'Typography', label: 'Типографика', icon: '🎨', desc: 'Наложение текста' },
    { mode: 'HeroesForge', label: 'Кузница Героев', icon: '⚒️', desc: 'Генерация персонажей в два этапа' }
  ];

  return (
    <aside className="w-72 bg-slate-950 border-r border-slate-800 flex flex-col justify-between h-full p-4 z-10">
      <div className="flex flex-col gap-6">
        <div className="px-2 py-4 border-b border-slate-800/80">
          <div className="flex items-center gap-2">
            <span className="text-2xl">📚</span>
            <h2 className="text-lg font-black tracking-wider bg-gradient-to-r from-indigo-400 to-cyan-400 bg-clip-text text-transparent">
              COVER STUDIO
            </h2>
          </div>
          <p className="text-[10px] uppercase tracking-widest text-slate-500 mt-1 font-bold">Beta 0.2 Ecosystem</p>
        </div>

        <nav className="flex flex-col gap-1.5">
          {menuItems.map((item) => {
            const isActive = activeMode === item.mode;
            return (
              <button
                key={item.mode}
                onClick={() => onModeChange(item.mode)}
                className={`w-full flex items-center gap-3.5 p-3 rounded-xl text-left transition-all duration-200 ${
                  isActive
                    ? 'bg-indigo-600/10 border border-indigo-500/40 text-white shadow-inner'
                    : 'border border-transparent text-slate-400 hover:bg-slate-900 hover:text-slate-200'
                }`}
              >
                <span className="text-xl flex-none">{item.icon}</span>
                <div className="overflow-hidden">
                  <p className={`text-sm font-bold ${isActive ? 'text-indigo-300' : 'text-slate-300'}`}>
                    {item.label}
                  </p>
                  <p className="text-[11px] text-slate-500 truncate mt-0.5">{item.desc}</p>
                </div>
              </button>
            );
          })}
        </nav>
      </div>

      <div className="bg-slate-900/60 border border-slate-800 p-3 rounded-xl flex items-center gap-3">
        <div className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse flex-none" />
        <div className="overflow-hidden">
          <p className="text-[11px] font-bold text-slate-400">Anti-F5 Persistence</p>
          <p className="text-[10px] text-slate-600 truncate font-mono">SQLite State Connected</p>
        </div>
      </div>
    </aside>
  );
}
