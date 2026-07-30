namespace MaterialLibrary.Crud

open MaterialLibrary

/// <summary>CRUD helpers for <see cref="LibraryConfiguration"/> XML files.</summary>
module ConfigurationCrud =
    /// <summary>Creates the default configuration in memory.</summary>
    let createDefault () =
        Configuration.createDefault ()

    /// <summary>Reads a configuration XML file and validates it.</summary>
    let read path =
        Configuration.load path

    /// <summary>Creates or updates a configuration XML file after validation.</summary>
    let save path config =
        Configuration.save path config

    /// <summary>Reads an existing configuration file or creates a default one when it is missing.</summary>
    let readOrCreateDefault path =
        Configuration.loadOrCreateDefault path

    /// <summary>Deletes a configuration XML file when present.</summary>
    let delete path =
        try
            if System.IO.File.Exists(path) then
                System.IO.File.Delete(path)

            Ok()
        with ex ->
            Error $"Could not delete configuration file '{path}': {ex.Message}"

