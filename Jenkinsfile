pipeline {
    agent { label 'unity' }

    parameters {
        choice(name: 'TARGET', choices: ['Android', 'StandaloneWindows'])
        string(name: 'VERSION', defaultValue: '0.0.1')
    }

    options {
        // Unity Library 재사용을 위해 workspace를 정리하지 않으므로 같은 workspace에서 동시에 빌드하지 않는다.
        disableConcurrentBuilds()
        skipDefaultCheckout()
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    stages {
        stage('Checkout') {
            steps {
                checkout([$class: 'GitSCM', branches: scm.branches, userRemoteConfigs: scm.userRemoteConfigs,
                    extensions: [[$class: 'GitLFSPull']]])
            }
        }

        stage('Build') {
            steps {
                // 인자 바인딩 오류는 기본적으로 종료 오류가 아니어서 exit $LASTEXITCODE가 0으로 끝나므로 Stop으로 실패시킨다.
                powershell '$ErrorActionPreference = \'Stop\'; & .\\Tools\\Build\\build.ps1 -Target $env:TARGET -Version $env:VERSION -BuildNumber $env:BUILD_NUMBER; exit $LASTEXITCODE'
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'Tools/Build/bin/*.log', allowEmptyArchive: true
        }
        success {
            archiveArtifacts artifacts: params.TARGET == 'Android' ? 'Client/Builds/AOS/**' : 'Client/Builds/EXE/**'
        }
    }
}
