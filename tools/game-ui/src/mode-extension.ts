/** One server mode, retaining its numeric identity independently of localized text. */
export interface ModeExtensionMode {
  id: number;
  key: string;
  names: Readonly<Record<string, string>>;
  tokens?: Readonly<Record<string, string>>;
  descriptions?: Readonly<Record<string, string>>;
}

/** Localized controls of the mode-selection dialog. */
export interface ModeExtensionLabels {
  title: string;
  confirm: string;
  cancel: string;
  close: string;
  scroll: string;
  previous: string;
  next: string;
  hidden: string;
}

/** Four original client frames, ordered up, down, hover and disabled. */
export interface ModeExtensionSkin {
  states: readonly string[];
  width: number;
  height: number;
}

/** Prepared modern button graphics; window resources remain selected by client theme. */
export interface ModeExtensionAppearance {
  skins: Readonly<Record<string, ModeExtensionSkin>>;
}
