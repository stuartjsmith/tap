import jenkins.model.Jenkins

pipeline {
	agent any
    stages {
	    stage('Build') {
		    steps {
                bat "\"${tool 'msbuild12'}\" tap.sln /p:Configuration=Release /p:Platform=x64 /p:BuildNumber=${env.BUILD_NUMBER}"
            }
	    }
	    stage('Archive') {
		    steps {
                archive 'bin/Release/**'
            }
	    }
	    stage('Deploy') {
		    steps {
                echo "deploying"
                //bat "xcopy bin\\Release\\InstallationUpdater.exe \\\\ukchesnetvault1\\DesignApplications\\Tools\\InstallationUpdater /y /f"
                //bat "xcopy bin\\Release\\InstallationUpdater.Configuration.xml \\\\ukchesnetvault1\\DesignApplications\\Tools\\InstallationUpdater /y /f"
            }
	    }  
	}
}