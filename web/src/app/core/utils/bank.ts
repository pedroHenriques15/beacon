/** Initials for a bank's avatar: 'BPI', 'ActivoBank' → 'AB', 'Trade Republic' → 'TR'. */
export function bankInitials(bank: string): string {
  const name = bank.trim();
  const words = name.split(/\s+/);
  if (words.length > 1) return (words[0][0] + words[1][0]).toUpperCase();
  if (name.length <= 3) return name.toUpperCase();
  const capitals = name.slice(1).match(/[A-Z]/);
  if (capitals && name !== name.toUpperCase()) return (name[0] + capitals[0]).toUpperCase();
  return name.slice(0, 2).toUpperCase();
}
