#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Graphics
{
	public sealed class CursorProvider
	{
		public readonly IReadOnlyDictionary<string, CursorSequence> Cursors;
		public readonly IReadOnlyDictionary<string, ImmutablePalette> Palettes;

		public CursorProvider(ModData modData)
		{
			var fileSystem = modData.DefaultFileSystem;
			var stringPool = new HashSet<string>(); // Reuse common strings in YAML
			var sequenceYaml = MiniYaml.Merge(modData.Manifest.Cursors.Select(
				s => MiniYaml.FromStream(fileSystem.Open(s), s, stringPool: stringPool)));

			var cursorsYaml = new MiniYaml(null, sequenceYaml).NodeWithKey("Cursors").Value;

			// Overwrite previous definitions if there are duplicates
			var pals = new Dictionary<string, IProvidesCursorPaletteInfo>();
			foreach (var p in modData.DefaultRules.Actors[SystemActors.World].TraitInfos<IProvidesCursorPaletteInfo>())
				if (p.Palette != null)
					pals[p.Palette] = p;

			Palettes = cursorsYaml.Nodes.Select(n => n.Value.Value)
				.Where(p => p != null)
				.Distinct()
				.Select(p => new { Name = p, Palette = pals.TryGetValue(p, out var pi) ? pi.ReadPalette(modData.DefaultFileSystem) : null })
				.Where(x => x.Palette != null)
				.ToDictionary(x => x.Name, x => x.Palette);

			var frameCache = new FrameCache(fileSystem, modData.SpriteLoaders);
			var cursors = new Dictionary<string, CursorSequence>();
			foreach (var s in cursorsYaml.Nodes)
			{
				var cursorSrc = s.Key;
				foreach (var sequence in s.Value.Nodes)
				{
					try
					{
						cursors.Add(sequence.Key, new CursorSequence(frameCache, sequence.Key, cursorSrc, s.Value.Value, sequence.Value));
					}
					catch (Exception)
					{
						// Skip cursor sequences with missing files (for headless mode compatibility)
					}
				}
			}

			Cursors = cursors;
		}

		public bool HasCursorSequence(string cursor)
		{
			return Cursors.ContainsKey(cursor);
		}

		public CursorSequence GetCursorSequence(string cursor)
		{
			try { return Cursors[cursor]; }
			catch (KeyNotFoundException)
			{
				throw new InvalidOperationException($"Cursor does not have a sequence `{cursor}`");
			}
		}
	}
}
