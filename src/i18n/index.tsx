import React, { createContext, useContext, useState, useEffect } from 'react';
import { Language, TranslationDictionary } from './types';
import { ptBR } from './ptBR';
import { enUS } from './enUS';

interface I18nContextType {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: TranslationDictionary;
}

const dictionaries: Record<Language, TranslationDictionary> = {
  'pt-BR': ptBR,
  'en-US': enUS,
};

const I18nContext = createContext<I18nContextType>({
  language: 'pt-BR',
  setLanguage: () => {},
  t: ptBR,
});

export const I18nProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [language, setLanguageState] = useState<Language>(() => {
    if (typeof window !== 'undefined') {
      const saved = localStorage.getItem('pc_control_lang') as Language;
      if (saved === 'pt-BR' || saved === 'en-US') {
        return saved;
      }
    }
    return 'pt-BR';
  });

  const setLanguage = (lang: Language) => {
    setLanguageState(lang);
    if (typeof window !== 'undefined') {
      localStorage.setItem('pc_control_lang', lang);
    }
  };

  const t = dictionaries[language] || ptBR;

  return (
    <I18nContext.Provider value={{ language, setLanguage, t }}>
      {children}
    </I18nContext.Provider>
  );
};

export const useI18n = () => useContext(I18nContext);
