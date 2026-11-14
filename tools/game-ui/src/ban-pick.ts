/** Categories shown by the Ban Pick editor. */
export type BanPickCategory = 'weapons' | 'characters' | 'clothes' | 'accessories';

/** One cell of a shared preview sheet: the sheet's URL and size, and the cell's rectangle in sheet pixels. */
export interface BanPickSprite {
  url: string;
  sheetWidth: number;
  sheetHeight: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** One client item with an externally prepared transparent preview: its own image, a sheet cell, or none. */
export interface BanPickItem {
  id: number;
  category: BanPickCategory;
  subCategory: string;
  name: string;
  preview: string | BanPickSprite | null;
  slogan: string;
  description: string;
  detail1: string;
  detail2: string;
  /** The weapon popup's five gauges (damage, accuracy, weight, fire rate, auto aim; 0-100) when the client shows them. */
  spec?: readonly number[];
  /** A clothing set: banning it bans these items. */
  members?: readonly number[];
  /** A set's armor gauges (armor, reload speed, move speed; 0-100). */
  setStats?: readonly number[] | null;
  /** The client's allowChr: FighterConfig names separated by spaces, or "all". */
  allow?: string;
}

/** A named selection supplied and persisted by the editor's host. */
export interface BanPickPreset {
  id: string;
  name: string;
  itemIds: number[];
}

/** Host-localized text for the reusable editor and its accessible controls. */
export interface BanPickLabels {
  title: string;
  hint: string;
  categories: Record<BanPickCategory, string>;
  subCategories: Record<string, string>;
  all: string;
  search: string;
  banned: string;
  allowed: string;
  selected: string;
  reset: string;
  apply: string;
  cancel: string;
  close: string;
  previous: string;
  next: string;
  page: string;
  empty: string;
  restrictedOnly: string;
  presets: string;
  presetName: string;
  choosePreset: string;
  savePreset: string;
  loadPreset: string;
  deletePreset: string;
  noDescription: string;
  /** Shown where an item has no preview in the selected client's pack (assets absent or not rendered). */
  noPreview?: string;
  /** The hint a member who cannot edit sees above the room's banned items. */
  bannedHint?: string;
  /** The weapon popup's five gauge names, in gauge order. */
  stats?: readonly string[];
  /** The armor popups' heading over the characters who can wear the item. */
  wearable?: string;
  /** The armor popups' closing notice. */
  wearNotice?: string;
  /** The set-armor popup's three gauge names. */
  armorStats?: readonly string[];
}

/** Native category sprite identities read from the selected client's category tables. */
export interface BanPickAppearance {
  theme: 'v1' | 'v2';
  categorySkins: Record<BanPickCategory, string>;
  categorySizes: Record<BanPickCategory, {width: number; height: number}>;
  nameBounds: {top: number; height: number};
  statusBounds: {top: number; height: number};
  titleBounds: {top: number; height: number};
  subCategorySkins: Record<string, string>;
  subCategoryOrder: Record<string, number>;
  actionSkins: Record<'Ok' | 'Cancel', string[]>;
  actionHeight: number;
  infoSize: {width: number; height: number};
  infoTitleBar: {skin: string; x: number; y: number; width: number; height: number} | null;
  /** The prepared sprites' file format; a WebP set keeps the same paths with a .webp extension. */
  imageFormat?: 'png' | 'webp';
}
