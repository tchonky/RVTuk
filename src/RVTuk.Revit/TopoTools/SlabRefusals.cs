using System;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Why a toposolid or floor cannot be worked on, in words that follow "Toposolid 123: ".
    /// Null means it can.
    ///
    /// Two levels, because the converter and the edit session want different things:
    /// <see cref="Common"/> is what both refuse (subdivision, model group, design option,
    /// borrowed); <see cref="Of"/> adds what only shape editing refuses — a floor that cannot take
    /// shape points, which in practice is a floor with a slope arrow. The converter must not get
    /// that one: converting a sloped floor is something it does on purpose (it reads the slope off
    /// the top face).
    /// </summary>
    public static class SlabRefusals
    {
        public const string SlopeArrow = "it has a slope arrow, and a sloped floor cannot take shape points";
        public const string NoShapePoints = "it cannot take shape points";

        /// <summary>
        /// Everything the converter and the session both refuse. Pure reads: no transaction needed.
        /// </summary>
        public static string? Common(Document doc, Element element)
        {
            if (element is Toposolid toposolid &&
                toposolid.HostTopoId != null && toposolid.HostTopoId != ElementId.InvalidElementId)
                return "it is a subdivision";

            if (element.GroupId != ElementId.InvalidElementId) return "it is in a model group";

            // Work happens in the option being edited (or the main model), so anything elsewhere
            // would be changed from outside its own option.
            var activeOptionId = DesignOption.GetActiveDesignOptionId(doc);
            var optionId = element.DesignOption?.Id ?? ElementId.InvalidElementId;
            if (optionId != activeOptionId)
            {
                return optionId == ElementId.InvalidElementId
                    ? "it is in the main model while a design option is being edited"
                    : "it is in a design option that is not being edited";
            }

            if (doc.IsWorkshared &&
                WorksharingUtils.GetCheckoutStatus(doc, element.Id, out string owner) == CheckoutStatus.OwnedByOtherUser)
                return $"it is borrowed by {Isolate(owner)}";

            return null;
        }

        /// <summary>
        /// <see cref="Common"/>, plus whether the element can take shape points at all.
        ///
        /// Whether a floor can is only known by trying: <c>Enable()</c> on its shape editor. That
        /// try is made inside a transaction that is always rolled back — a
        /// <see cref="SubTransaction"/> when the caller already has one open, otherwise a
        /// <see cref="Transaction"/> of its own — so this may be called either inside or outside a
        /// transaction and never changes the model. An editor that is already enabled needs no try.
        /// </summary>
        public static string? Of(Document doc, Element element)
        {
            var common = Common(doc, element);
            if (common != null) return common;

            var editor = SlabShape.EditorOf(element);
            if (editor == null) return element is Floor floor && IsTilted(floor) ? SlopeArrow : NoShapePoints;
            if (editor.IsEnabled) return null;

            if (TryEnable(doc, editor) != false) return null;
            return element is Floor sloped && IsTilted(sloped) ? SlopeArrow : NoShapePoints;
        }

        /// <returns>True when <c>Enable()</c> succeeded, false when it threw, null when the try
        /// itself could not be made (no transaction could be opened) — then nothing is refused.</returns>
        private static bool? TryEnable(Document doc, SlabShapeEditor editor)
        {
            try
            {
                if (doc.IsModifiable)
                {
                    using (var sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try { return Enables(editor); }
                        finally { sub.RollBack(); }
                    }
                }

                using (var tx = new Transaction(doc, "Topo Tools — check shape points"))
                {
                    tx.Start();
                    try { return Enables(editor); }
                    finally { tx.RollBack(); }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Enables(SlabShapeEditor editor)
        {
            try
            {
                editor.Enable();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>A planar top face that is not horizontal: the mark of a slope arrow.</summary>
        private static bool IsTilted(Floor floor)
        {
            try
            {
                foreach (var reference in HostObjectUtils.GetTopFaces(floor))
                {
                    if (floor.GetGeometryObjectFromReference(reference) is PlanarFace face &&
                        Math.Abs(face.FaceNormal.Z) < 1 - 1e-9)
                        return true;
                }
            }
            catch (Exception)
            {
                // No readable top: cannot tell, so the generic wording is used.
            }
            return false;
        }

        /// <summary>First-strong isolates (U+2068…U+2069): a Hebrew user name ending in a number
        /// would otherwise drag the text after it into its right-to-left run.</summary>
        private static string Isolate(string text) => "⁨" + text + "⁩";
    }
}
