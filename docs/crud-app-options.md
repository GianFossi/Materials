# CRUD Application Options and User Guidance

The WPF application provides English descriptions in its main editing windows. Hover over buttons and input fields for additional context, including the units and effect of each action.

## Open application options

From the main window, choose **Options → Application options...**. Options are stored in:

```text
%LOCALAPPDATA%\MaterialLibrary\crud-options.xml
```

The application creates this XML file with defaults on first launch. Changes made in the Options window are written when **Save** is selected. The XML can be inspected or backed up, but invalid or incomplete values are reported rather than silently replaced.

## Product-form choices

The **Product forms** list defines the entries offered in the material editor and the ASME S1/S2 curve editor. Use the input field and **Add** to add a choice, or select a row and choose **Remove selected**. Keep at least one entry. Removing a choice does not change previously saved material or curve values; those existing values remain visible in their editor.

Default choices include Plate, Pipe, Tube, Forging, Bar, Casting, Fittings, seamless and welded pipe/tube, and Bolting. Add project-specific forms as needed.

## Welded-joint efficiency

**Default efficiency (joint)** is the reference efficiency for the longitudinal joint, or the joint most highly stressed by pressure, shown for welded S1/S2 curves when the selected curve's source notes do not state an explicit factor. The default is **0.70** and the accepted range is greater than 0 through 1.

This setting is guidance only. It does not alter curve values, automatically reduce stress, or override a factor and conditions in applicable ASME notes. Verify the source note and design conditions before use.

## Default file locations

- **Flex/property XML folder** is used as the initial folder when browsing for physical-property and external-pressure XML imports.
- **SQLite database file** is preselected in the database manager's open-file dialog. This preference is a file path, not a database copy; database edits continue to target the application's working copy.

Either path may be left blank. A configured path that is unavailable is not opened automatically; the user can select another location in the file dialog.

## Main windows

- **Material editor:** Choose the product form from the configured dropdown. Specification, grade, condition, and UNS identify the material; room-temperature tensile fields are separate from the temperature-dependent tables.
- **Material tables:** Select a dataset tab and use the displayed units. Tooltips explain row/table operations and specialized inputs.
- **ASME 2025 editor:** Choose an identity, property family, and curve variant. The curve list distinguishes product form and size range; the point grid edits temperature/value pairs. Original source notes are read-only, while user notes are separately editable.
- **XML import:** Choose an XML folder, review the parsed source record and its match evidence, inspect the graph and units, and explicitly select destination materials. Importing requires review and confirmation; replacing an existing association is a separate opt-in.
- **Database manager:** Browse the protected working copy. Table and SQL changes affect that copy, not the source file selected by the user.
